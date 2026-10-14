using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;

/// <summary>Claims the event's meeting row atomically, then provisions (spec §3.2.2). A request that
/// doesn't win the claim makes no provider call.</summary>
public sealed class CreateEventMeetingCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    IExternalCalendarConnectionRepository connections,
    ICalendarEventMeetingRepository meetings,
    EventMeetingLinkService links,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
    IDateTimeProvider clock,
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

        var organizer = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        var organizerName = organizer is null ? "Someone" : $"{organizer.FirstName} {organizer.LastName}";
        var now = clock.UtcNow;

        var snapshot = await meetings.GetByCalendarEventAsync(tenantId, existing.Id, ct);
        if (snapshot is not null)
        {
            snapshot = await links.ExpireIfEndedAsync(existing, snapshot, ct);
            switch (snapshot.Status)
            {
                case CalendarEventMeetingStatuses.Active:
                    return Result<CreateEventMeetingResult>.Success(new CreateEventMeetingResult(snapshot.JoinUrl));
                case CalendarEventMeetingStatuses.ProvisioningUnknown:
                    return Result<CreateEventMeetingResult>.Conflict("meeting_link_status_unknown");
                case CalendarEventMeetingStatuses.Unresolved:
                    return Result<CreateEventMeetingResult>.Conflict("meeting_link_unresolved");
                case CalendarEventMeetingStatuses.Provisioning:
                    // Someone else's request owns the row. Only once it is stale (its request crashed, or its
                    // save failed after the provider call) does it become provisioning_unknown, for Retry/Remove.
                    if (await meetings.TryMarkStaleProvisioningUnknownAsync(tenantId, snapshot.Id, now - EventMeetingLinkService.StaleAfter, now, ct))
                    {
                        await SendFallbackAsync(existing, organizerName, ct, CalendarMeetingNoticeCodes.LinkUnknown, snapshot.Provider);
                        return Result<CreateEventMeetingResult>.Conflict("meeting_link_status_unknown");
                    }
                    return Result<CreateEventMeetingResult>.Conflict("meeting_link_in_progress");
            }
        }

        var isZoom = request.Provider == CalendarEventMeetingProviders.Zoom;
        var connection = await connections.GetByTenantUserProviderAsync(
            tenantId, currentUser.UserId, isZoom ? CalendarExternalSources.Zoom : CalendarExternalSources.OutlookCalendar, ct);
        if (connection is null
            || connection.Status != ExternalCalendarConnectionStatuses.Active
            || !HasMeetingScope(connection.ScopesJson, isZoom ? "meeting:write:meeting" : "OnlineMeetings.ReadWrite"))
        {
            // P1-5: the one-click create flow held invites back for the link; send them without one.
            await SendFallbackAsync(existing, organizerName, ct);
            return Result<CreateEventMeetingResult>.Conflict("meeting_provider_not_connected");
        }

        if (snapshot is null)
        {
            var row = new CalendarEventMeeting
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventId = existing.Id, ExternalCalendarConnectionId = connection.Id,
                Provider = request.Provider, Status = CalendarEventMeetingStatuses.Provisioning, LinkGeneration = 1,
                ProvisioningStartedAt = now, CreatedAt = now
            };
            if (!await meetings.TryInsertProvisioningAsync(row, ct))
                return Result<CreateEventMeetingResult>.Conflict("meeting_link_in_progress");
        }
        else if (await meetings.TryClaimNewGenerationAsync(tenantId, snapshot.Id, connection.Id, request.Provider, now, ct) is null)
        {
            return Result<CreateEventMeetingResult>.Conflict("meeting_link_in_progress");
        }

        var claimed = (await meetings.GetTrackedByCalendarEventAsync(tenantId, existing.Id, ct))!;
        return await links.ProvisionClaimedAsync(existing, claimed, connection, recovery: false, organizerName, ct);
    }

    /// <summary>P1-5 fallback in its own transaction (keyed emails need one); with a notice code the
    /// organizer is also told why there is no link.</summary>
    private Task SendFallbackAsync(
        CalendarEvent evt, string organizerName, CancellationToken ct, string? noticeCode = null, string? provider = null)
        => unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await links.SendFallbackInvitesAsync(evt, organizerName, innerCt);
            if (noticeCode is not null)
                await links.NotifyOrganizerAsync(evt, noticeCode, provider!, innerCt);
            await unitOfWork.SaveChangesAsync(innerCt);
            return true;
        }, ct);

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
