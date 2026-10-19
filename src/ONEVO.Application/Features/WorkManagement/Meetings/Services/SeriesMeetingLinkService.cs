using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>The keyed outbox message that provisions one generation of a series link after its claim committed.</summary>
public sealed record MeetingLinkProvisionPayload(Guid TenantId, Guid LinkId, Guid SeriesId, int Generation);

/// <summary>
/// A series-owned Teams/Zoom link (spec 5.7) after its row is claimed: the provider call (the Phase 0 provisioner, with the
/// link id as the correlation id), then the row's next state and who is told what - the same outcome table as calendar
/// events (EventMeetingLinkService), for a series owner. Stateless.
/// </summary>
public sealed class SeriesMeetingLinkService
{
    private readonly IMeetingLinkRepository _links;
    private readonly IMeetingRepository _meetings;
    private readonly IMeetingOccurrenceWriter _writer;
    private readonly IExternalCalendarConnectionRepository _connections;
    private readonly IMeetingProvisioner _provisioner;
    private readonly IMeetingNotifier _notifier;
    private readonly IMeetingRecurrence _recurrence;
    private readonly IEmployeeRepository _employees;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public SeriesMeetingLinkService(IMeetingLinkRepository links, IMeetingRepository meetings, IMeetingOccurrenceWriter writer,
        IExternalCalendarConnectionRepository connections, IMeetingProvisioner provisioner, IMeetingNotifier notifier,
        IMeetingRecurrence recurrence, IEmployeeRepository employees, IOutboxWriter outbox, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _links = links;
        _meetings = meetings;
        _writer = writer;
        _connections = connections;
        _provisioner = provisioner;
        _notifier = notifier;
        _recurrence = recurrence;
        _employees = employees;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public static MeetingLinkResponse Describe(CalendarEventMeeting link, bool releasePending = false)
        => new(link.Id, link.Provider, link.Status, string.IsNullOrEmpty(link.JoinUrl) ? null : link.JoinUrl, link.OrganizerJoinUrl,
            link.FailureReason, link.LinkGeneration, releasePending);

    /// <summary>Queues the provider call for a claimed generation; runs after the claim commits. Needs the caller's
    /// transaction (keyed message).</summary>
    public Task QueueProvisioningAsync(Guid tenantId, Guid linkId, Guid seriesId, int generation, CancellationToken ct)
        => _outbox.EnqueueKeyedAsync(OutboxMessageTypes.MeetingLinkProvision, new MeetingLinkProvisionPayload(tenantId, linkId, seriesId, generation),
            $"meeting-link-provision:{linkId}:{generation}", tenantId, ct);

    /// <summary>The provider call for a link in provisioning, then its outcome. A no-op (returning the link as it is) when
    /// the row is no longer provisioning - someone else already settled this generation.</summary>
    public async Task<Result<MeetingLinkResponse>> ProvisionAsync(Guid tenantId, Guid linkId, Guid seriesId, bool recovery, CancellationToken ct)
    {
        var snapshot = await _links.GetAsync(tenantId, linkId, ct);
        if (snapshot is null)
            return MeetingReadService.NotFound<MeetingLinkResponse>();
        if (snapshot.Status != CalendarEventMeetingStatuses.Provisioning)
            return Result<MeetingLinkResponse>.Success(Describe(snapshot));
        var series = await _meetings.GetSeriesAsync(tenantId, seriesId, ct);
        if (series is null)
            return MeetingReadService.NotFound<MeetingLinkResponse>();

        var connection = await _connections.GetByIdForTenantAsync(tenantId, snapshot.ExternalCalendarConnectionId, ct);
        var outcome = connection is null || !CalendarMeetingConnections.CanCreateMeetings(connection, snapshot.Provider)
            ? new MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind.NotConnected, null, CalendarMeetingLinkStates.NotConnected)
            : await _provisioner.ProvisionAsync(new MeetingProvisioningRequest(connection, snapshot.Provider, snapshot.Id, snapshot.LinkGeneration,
                series.Title, NextStart(series), NextStart(series).AddMinutes(series.DurationMinutes), recovery,
                await AttendeeEmailsAsync(series, ct)), CancellationToken.None);

        // The provider call can take up to 90 s: decide on the committed state, under the series lock. Runs to completion
        // even when the request is cancelled, or the claimed row would be stranded in provisioning.
        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await _writer.LockSeriesAsync(tenantId, seriesId, innerCt);
            var link = await _links.GetTrackedAsync(tenantId, linkId, innerCt);
            if (link is null || link.Status != CalendarEventMeetingStatuses.Provisioning || link.LinkGeneration != snapshot.LinkGeneration)
                return link is null ? MeetingReadService.NotFound<MeetingLinkResponse>() : Result<MeetingLinkResponse>.Success(Describe(link));
            var current = await _meetings.GetSeriesAsync(tenantId, seriesId, innerCt);
            var now = _clock.UtcNow;
            link.UpdatedAt = now;

            if (outcome.Kind == MeetingProvisioningOutcomeKind.Created)
            {
                var meeting = outcome.Meeting!;
                link.ExternalMeetingId = meeting.ExternalMeetingId;
                link.JoinUrl = meeting.JoinUrl;
                link.OrganizerJoinUrl = meeting.OrganizerJoinUrl;
                link.PasscodeOrPin = meeting.PasscodeOrPin;
                link.FailureReason = null;
                link.ProvisioningStartedAt = null;
                link.InvitesPending = false;
                if (current is null || current.CancelledAt is not null)
                {
                    // Cancelled during the call: nobody gets a link to it, and the new remote meeting goes away.
                    link.Status = CalendarEventMeetingStatuses.Cancelled;
                    await QueueProviderCancelAsync(tenantId, link, current ?? series, innerCt);
                    await _unitOfWork.SaveChangesAsync(innerCt);
                    return Result<MeetingLinkResponse>.Success(Describe(link));
                }
                link.Status = CalendarEventMeetingStatuses.Active;
                await _unitOfWork.SaveChangesAsync(innerCt);
                await _notifier.LinkReadyAsync(current, link, innerCt);
                await _unitOfWork.SaveChangesAsync(innerCt);
                return Result<MeetingLinkResponse>.Success(Describe(link));
            }

            var ambiguous = outcome.Kind == MeetingProvisioningOutcomeKind.Ambiguous;
            link.Status = ambiguous ? CalendarEventMeetingStatuses.ProvisioningUnknown : CalendarEventMeetingStatuses.Failed;
            link.FailureReason = outcome.Reason;
            if (!ambiguous)
                link.ProvisioningStartedAt = null;
            var heldBack = link.InvitesPending;
            link.InvitesPending = false;
            await _unitOfWork.SaveChangesAsync(innerCt);
            if (current is { CancelledAt: null })
            {
                // P1-5: nobody may be left uninvited because the link did not happen - once.
                if (heldBack)
                    await _notifier.InviteAsync(current,
                        (await _meetings.ListSeriesParticipantsAsync(tenantId, seriesId, innerCt)).Where(p => !p.IsDeleted).ToList(),
                        await _meetings.ListSeriesGuestsAsync(tenantId, seriesId, innerCt), innerCt);
                await _notifier.NotifyOrganizerAsync(current,
                    ambiguous ? CalendarMeetingNoticeCodes.LinkUnknown : CalendarMeetingNoticeCodes.LinkFailed, link.Provider, innerCt);
            }
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result<MeetingLinkResponse>.Success(Describe(link));
        }, CancellationToken.None);
    }

    /// <summary>Keyed meeting-cancel:{linkId}:{generation} through the Phase 0 cancel handler (retries, parking, notice).</summary>
    public async Task QueueProviderCancelAsync(Guid tenantId, CalendarEventMeeting link, MeetingSeries series, CancellationToken ct)
    {
        var organizer = (await _employees.ListByIdsAsync(tenantId, [series.OrganizerEmployeeId], ct)).GetValueOrDefault(series.OrganizerEmployeeId);
        await _outbox.EnqueueKeyedAsync(OutboxMessageTypes.CalendarMeetingCancel,
            new CalendarMeetingCancelPayload(tenantId, link.Provider, link.ExternalMeetingId, link.ExternalCalendarConnectionId,
                organizer?.UserId ?? Guid.Empty, series.OrganizerEmployeeId, series.Title),
            $"meeting-cancel:{link.Id}:{link.LinkGeneration}", tenantId, ct);
    }

    /// <summary>The time the provider meeting is scheduled for: the next occurrence, or the first one when none is left.</summary>
    private DateTimeOffset NextStart(MeetingSeries series)
    {
        var now = _clock.UtcNow;
        return _recurrence.Occurrences(series.RecurrenceRule, series.DtStart, series.Timezone, now, now.AddDays(400))
            .Select(o => (DateTimeOffset?)o).FirstOrDefault() ?? series.DtStart;
    }

    /// <summary>Series attendees' emails, so the provider recognises them when they join (plan C1 P2-3).</summary>
    private async Task<IReadOnlyList<string>> AttendeeEmailsAsync(MeetingSeries series, CancellationToken ct)
    {
        var participants = (await _meetings.ListSeriesParticipantsAsync(series.TenantId, series.Id, ct)).Where(p => !p.IsDeleted)
            .Select(p => p.EmployeeId).ToList();
        var people = await _employees.ListByIdsAsync(series.TenantId, participants, ct);
        return people.Values.Select(e => e.Email).Where(e => !string.IsNullOrWhiteSpace(e)).Cast<string>()
            .Concat((await _meetings.ListSeriesGuestsAsync(series.TenantId, series.Id, ct)).Select(g => g.Email))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
