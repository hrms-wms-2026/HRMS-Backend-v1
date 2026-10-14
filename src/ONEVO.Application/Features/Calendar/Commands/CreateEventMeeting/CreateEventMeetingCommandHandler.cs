using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;

public sealed class CreateEventMeetingCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    IExternalCalendarConnectionRepository connections,
    ICalendarConnectionTokenProvider tokenProvider,
    ITeamsMeetingClient teamsClient,
    IZoomMeetingClient zoomClient,
    ICalendarEventMeetingRepository meetings,
    IUnitOfWork unitOfWork,
    ICalendarNotificationSender notifications,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees)
    : IRequestHandler<CreateEventMeetingCommand, Result<CreateEventMeetingResult>>
{
    public async Task<Result<CreateEventMeetingResult>> Handle(CreateEventMeetingCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CreateEventMeetingResult>.Forbidden();

        var tenantId = currentUser.TenantId;
        var existing = await events.GetTrackedByIdForTenantAsync(tenantId, request.EventId, ct);
        if (existing is null)
            return Result<CreateEventMeetingResult>.NotFound("Calendar event not found.");

        if (existing.CreatedById != currentUser.UserId)
            return Result<CreateEventMeetingResult>.Forbidden("Only the event organizer can add a meeting.");

        var isZoom = request.Provider == CalendarEventMeetingProviders.Zoom;
        var externalSource = isZoom ? CalendarExternalSources.Zoom : CalendarExternalSources.OutlookCalendar;
        var oauthProvider = isZoom ? "zoom" : "microsoft";
        var requiredScope = isZoom ? "meeting:write:meeting" : "OnlineMeetings.ReadWrite";

        var existingMeeting = await meetings.GetTrackedByCalendarEventAsync(tenantId, existing.Id, ct);
        if (existingMeeting is { Status: CalendarEventMeetingStatuses.Active })
            return Result<CreateEventMeetingResult>.Conflict("meeting_already_exists");

        var connection = await connections.GetByTenantUserProviderAsync(tenantId, currentUser.UserId, externalSource, ct);
        if (connection is null
            || connection.Status != ExternalCalendarConnectionStatuses.Active
            || !HasMeetingScope(connection.ScopesJson, requiredScope))
        {
            return await LinkFailedAsync(tenantId, existing, request, Result<CreateEventMeetingResult>.Conflict("meeting_provider_not_connected"), ct);
        }

        var accessToken = await tokenProvider.GetFreshAccessTokenAsync(connection, oauthProvider, ct);
        if (accessToken is null)
            return await LinkFailedAsync(tenantId, existing, request, Result<CreateEventMeetingResult>.Conflict("meeting_provider_not_connected"), ct);

        // The people invited to the event are added to the meeting too (plan C1 P2-3).
        var participantsByEvent = await events.GetParticipantsForEventsAsync(tenantId, [existing.Id], ct);
        var participantEmployeeIds = participantsByEvent.TryGetValue(existing.Id, out var p) ? p.Select(x => x.EmployeeId).ToList() : [];
        var guestsByEvent = await events.GetGuestsForEventsAsync(tenantId, [existing.Id], ct);
        var guestEmails = guestsByEvent.TryGetValue(existing.Id, out var g) ? g.Select(x => x.Email).ToList() : [];
        var attendeeEmails = new List<string>(guestEmails);
        foreach (var employeeId in participantEmployeeIds)
        {
            if (await employees.GetByIdAsync(tenantId, employeeId, ct) is { Email: { Length: > 0 } email })
                attendeeEmails.Add(email);
        }

        string externalMeetingId, joinUrl;
        string? organizerJoinUrl, passcode;
        try
        {
            if (isZoom)
            {
                var dto = await zoomClient.CreateMeetingAsync(accessToken, existing.Title, existing.StartDate, existing.EndDate, ct, attendeeEmails);
                (externalMeetingId, joinUrl, organizerJoinUrl, passcode) = (dto.ExternalMeetingId, dto.JoinUrl, dto.OrganizerJoinUrl, dto.PasscodeOrPin);
            }
            else
            {
                var dto = await teamsClient.CreateMeetingAsync(accessToken, existing.Title, existing.StartDate, existing.EndDate, ct, attendeeEmails);
                (externalMeetingId, joinUrl, organizerJoinUrl, passcode) = (dto.ExternalMeetingId, dto.JoinUrl, dto.OrganizerJoinUrl, dto.PasscodeOrPin);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await LinkFailedAsync(tenantId, existing, request, Result<CreateEventMeetingResult>.Failure("meeting_creation_failed", 502), ct);
        }

        var organizerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        var organizerName = organizerEmployee is null ? "Someone" : $"{organizerEmployee.FirstName} {organizerEmployee.LastName}";

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            existing.MeetingLink = joinUrl;
            events.Update(existing);

            if (existingMeeting is not null)
            {
                // One row per event (unique index): a previously removed (cancelled) meeting is reused.
                existingMeeting.ExternalCalendarConnectionId = connection.Id;
                existingMeeting.Provider = request.Provider;
                existingMeeting.ExternalMeetingId = externalMeetingId;
                existingMeeting.JoinUrl = joinUrl;
                existingMeeting.OrganizerJoinUrl = organizerJoinUrl;
                existingMeeting.PasscodeOrPin = passcode;
                existingMeeting.Status = CalendarEventMeetingStatuses.Active;
                existingMeeting.LastAttendanceSyncedAt = null;
                existingMeeting.UpdatedAt = DateTimeOffset.UtcNow;
                meetings.Update(existingMeeting);
            }
            else
            {
                await meetings.AddAsync(new CalendarEventMeeting
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventId = existing.Id,
                    ExternalCalendarConnectionId = connection.Id, Provider = request.Provider,
                    ExternalMeetingId = externalMeetingId, JoinUrl = joinUrl,
                    OrganizerJoinUrl = organizerJoinUrl, PasscodeOrPin = passcode,
                    Status = CalendarEventMeetingStatuses.Active, CreatedAt = DateTimeOffset.UtcNow
                }, innerCt);
            }

            if (participantEmployeeIds.Count > 0)
            {
                await notifications.NotifyMeetingLinkAddedAsync(
                    tenantId, existing.Title, existing.StartDate, existing.Location,
                    participantEmployeeIds, organizerName, joinUrl, innerCt,
                    CalendarEventIcs.For(existing.Id, existing.EndDate, existing.RecurrenceRule, DateTimeOffset.UtcNow));
            }

            if (guestEmails.Count > 0)
            {
                await notifications.NotifyGuestsAsync(
                    tenantId, existing.Title, existing.StartDate, existing.Location,
                    guestEmails, organizerName, joinUrl, innerCt,
                    CalendarEventIcs.For(existing.Id, existing.EndDate, existing.RecurrenceRule, DateTimeOffset.UtcNow));
            }

            await unitOfWork.SaveChangesAsync(innerCt);
            return Result<CreateEventMeetingResult>.Success(new CreateEventMeetingResult(joinUrl));
        }, ct);
    }

    /// <summary>The Teams/Zoom link could not be created (plan C1 P1-5). When the event's invites were held back for it, they
    /// go out now without a link (with their .ics) and the organizer is alerted; the failure is still returned.</summary>
    private async Task<Result<CreateEventMeetingResult>> LinkFailedAsync(
        Guid tenantId, CalendarEvent calendarEvent, CreateEventMeetingCommand request, Result<CreateEventMeetingResult> failure, CancellationToken ct)
    {
        if (!request.InvitesHeldBack)
            return failure;
        var participants = await events.GetParticipantsForEventsAsync(tenantId, [calendarEvent.Id], ct);
        var employeeIds = participants is not null && participants.TryGetValue(calendarEvent.Id, out var list) ? list.Select(x => x.EmployeeId).ToList() : [];
        var guestEmails = await CalendarEventAudience.GuestEmailsAsync(events, tenantId, calendarEvent.Id, ct);
        var organizerName = await CalendarOrganizer.NameAsync(employees, tenantId, currentUser.UserId, ct);
        await notifications.EmailEventChangedAsync(tenantId, CalendarEventEmailKinds.Invite,
            CalendarEventIcs.For(calendarEvent.Id, calendarEvent.EndDate, calendarEvent.RecurrenceRule, DateTimeOffset.UtcNow),
            calendarEvent.Title, calendarEvent.StartDate, calendarEvent.Location, null, employeeIds, guestEmails, organizerName, ct);
        await notifications.NotifyMeetingLinkFailedAsync(tenantId, currentUser.UserId, calendarEvent.Title,
            request.Provider == CalendarEventMeetingProviders.Zoom ? "Zoom" : "Microsoft Teams", ct);
        await unitOfWork.SaveChangesAsync(ct);
        return failure;
    }

    private static bool HasMeetingScope(string scopesJson, string requiredScope)
    {
        try
        {
            var scopes = JsonSerializer.Deserialize<string[]>(scopesJson) ?? [];
            return scopes.Contains(requiredScope, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
