using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
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
    IUnitOfWork unitOfWork)
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

        var connection = await connections.GetByTenantUserProviderAsync(tenantId, currentUser.UserId, externalSource, ct);
        if (connection is null
            || connection.Status != ExternalCalendarConnectionStatuses.Active
            || !HasMeetingScope(connection.ScopesJson, requiredScope))
        {
            return Result<CreateEventMeetingResult>.Conflict("meeting_provider_not_connected");
        }

        var accessToken = await tokenProvider.GetFreshAccessTokenAsync(connection, oauthProvider, ct);
        if (accessToken is null)
            return Result<CreateEventMeetingResult>.Conflict("meeting_provider_not_connected");

        string externalMeetingId, joinUrl;
        string? organizerJoinUrl, passcode;
        if (isZoom)
        {
            var dto = await zoomClient.CreateMeetingAsync(accessToken, existing.Title, existing.StartDate, existing.EndDate, ct);
            (externalMeetingId, joinUrl, organizerJoinUrl, passcode) = (dto.ExternalMeetingId, dto.JoinUrl, dto.OrganizerJoinUrl, dto.PasscodeOrPin);
        }
        else
        {
            var dto = await teamsClient.CreateMeetingAsync(accessToken, existing.Title, existing.StartDate, existing.EndDate, ct);
            (externalMeetingId, joinUrl, organizerJoinUrl, passcode) = (dto.ExternalMeetingId, dto.JoinUrl, dto.OrganizerJoinUrl, dto.PasscodeOrPin);
        }

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            existing.MeetingLink = joinUrl;
            events.Update(existing);

            await meetings.AddAsync(new CalendarEventMeeting
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventId = existing.Id,
                ExternalCalendarConnectionId = connection.Id, Provider = request.Provider,
                ExternalMeetingId = externalMeetingId, JoinUrl = joinUrl,
                OrganizerJoinUrl = organizerJoinUrl, PasscodeOrPin = passcode,
                Status = CalendarEventMeetingStatuses.Active, CreatedAt = DateTimeOffset.UtcNow
            }, innerCt);

            await unitOfWork.SaveChangesAsync(innerCt);
            return Result<CreateEventMeetingResult>.Success(new CreateEventMeetingResult(joinUrl));
        }, ct);
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
