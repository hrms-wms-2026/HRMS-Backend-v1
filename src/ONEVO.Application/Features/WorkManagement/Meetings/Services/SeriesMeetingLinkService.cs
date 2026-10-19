using Microsoft.Extensions.Options;
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
/// events (EventMeetingLinkService), for a series owner. Also the organizer's link endpoints (create or regenerate, retry,
/// remove, cleanup confirmed) and the release rule: a series lets go of only its own reference, and the provider meeting is
/// cancelled when the last reference goes and nothing still needs the link (spec 5.7). Stateless.
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
    private readonly IMeetingAccessResolver _access;
    private readonly IOptions<MeetingsOptions> _options;

    public SeriesMeetingLinkService(IMeetingLinkRepository links, IMeetingRepository meetings, IMeetingOccurrenceWriter writer,
        IExternalCalendarConnectionRepository connections, IMeetingProvisioner provisioner, IMeetingNotifier notifier,
        IMeetingRecurrence recurrence, IEmployeeRepository employees, IOutboxWriter outbox, IUnitOfWork unitOfWork, IDateTimeProvider clock,
        IMeetingAccessResolver access, IOptions<MeetingsOptions> options)
    {
        _access = access;
        _options = options;
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
            // A split during the call shares the link: every open series that references it gets the outcome.
            var open = await OpenReferencingSeriesAsync(tenantId, linkId, innerCt);
            var current = open.FirstOrDefault(s => s.Id == seriesId) ?? open.FirstOrDefault();
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
                if (current is null)
                {
                    // Every series using it was cancelled during the call: nobody gets a link, the new remote meeting goes away.
                    link.Status = CalendarEventMeetingStatuses.Cancelled;
                    await QueueProviderCancelAsync(tenantId, link, series, innerCt);
                    await _unitOfWork.SaveChangesAsync(innerCt);
                    return Result<MeetingLinkResponse>.Success(Describe(link));
                }
                link.Status = CalendarEventMeetingStatuses.Active;
                await _unitOfWork.SaveChangesAsync(innerCt);
                foreach (var referencing in open)
                    await _notifier.LinkReadyAsync(referencing, link, innerCt);
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
            if (current is not null)
            {
                // P1-5: nobody may be left uninvited because the link did not happen - once (invites are keyed per row).
                if (heldBack)
                    await SendHeldBackInvitesAsync(tenantId, open, innerCt);
                await _notifier.NotifyOrganizerAsync(current,
                    ambiguous ? CalendarMeetingNoticeCodes.LinkUnknown : CalendarMeetingNoticeCodes.LinkFailed, link.Provider, innerCt);
            }
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result<MeetingLinkResponse>.Success(Describe(link));
        }, CancellationToken.None);
    }

    /// <summary>GET link: the join URL for every attendee while the link is active; the full state for the organizer only.</summary>
    public async Task<Result<MeetingSeriesLinkResponse>> GetAsync(Guid tenantId, Guid callerEmployeeId, Guid seriesId, CancellationToken ct)
    {
        var access = await _access.ResolveForSeriesAsync(tenantId, callerEmployeeId, seriesId, ct);
        if (access is null)
            return MeetingReadService.NotFound<MeetingSeriesLinkResponse>();
        var link = await SettleStaleAsync(tenantId, await _links.GetActiveForSeriesAsync(tenantId, seriesId, ct), ct);
        var joinUrl = link is { Status: CalendarEventMeetingStatuses.Active } && !string.IsNullOrEmpty(link.JoinUrl) ? link.JoinUrl : null;
        return Result<MeetingSeriesLinkResponse>.Success(new MeetingSeriesLinkResponse(joinUrl,
            access.Role == MeetingRoles.Organizer && link is not null ? Describe(link) : null));
    }

    /// <summary>POST link: a link for a series without a usable one; with Regenerate a new link for this series only - its
    /// reference to the old link is released (other series keep it) and a new one is claimed. Provisioning is queued.</summary>
    public async Task<Result<MeetingLinkResponse>> RegenerateAsync(Guid tenantId, Guid callerEmployeeId, Guid callerUserId, Guid seriesId,
        MeetingLinkRequest request, CancellationToken ct)
    {
        if (MeetingGuard.CheckWritable<MeetingLinkResponse>(_options.Value) is { } blocked)
            return blocked;
        if (request.Platform is not (CalendarEventMeetingProviders.MicrosoftTeams or CalendarEventMeetingProviders.Zoom))
            return Result<MeetingLinkResponse>.Failure("Choose Microsoft Teams or Zoom.", 400, MeetingErrorCodes.PlatformInvalid);
        if (await OrganizerOnlyAsync<MeetingLinkResponse>(tenantId, callerEmployeeId, seriesId, ct) is { } denied)
            return denied;
        var connection = await _connections.GetByTenantUserProviderAsync(tenantId, callerUserId,
            CalendarMeetingConnections.SourceFor(request.Platform), ct);
        if (!CalendarMeetingConnections.CanCreateMeetings(connection, request.Platform))
            return Result<MeetingLinkResponse>.Failure("Connect your calendar account to add an online meeting.", 409,
                MeetingErrorCodes.ProviderNotConnected);
        await SettleStaleAsync(tenantId, await _links.GetActiveForSeriesAsync(tenantId, seriesId, ct), ct);

        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                await _writer.LockSeriesAsync(tenantId, seriesId, innerCt);
                var series = await _meetings.GetSeriesAsync(tenantId, seriesId, innerCt);
                if (series is null)
                    return MeetingReadService.NotFound<MeetingLinkResponse>();
                if (series.CancelledAt is not null)
                    return Result<MeetingLinkResponse>.Failure("This meeting is cancelled.", 409, MeetingErrorCodes.MeetingCancelled);

                var now = _clock.UtcNow;
                var existing = await _links.GetActiveForSeriesAsync(tenantId, seriesId, innerCt);
                if (existing is not null)
                {
                    if (Unsettled<MeetingLinkResponse>(existing.Status) is { } unsettled)
                        return unsettled;
                    if (existing.Status == CalendarEventMeetingStatuses.Active && !request.Regenerate)
                        return Result<MeetingLinkResponse>.Failure("This meeting already has a link.", 409, MeetingErrorCodes.LinkExists);
                    // Its upcoming occurrences stop using the old link first, so they do not keep it "needed".
                    await _links.SetOccurrenceLinkAsync(tenantId, seriesId, null, now, _options.Value.Attendance.Checkpoints[0], innerCt);
                    await ReleaseReferenceAsync(tenantId, series, existing.Id, now, innerCt);
                }

                var linkId = await _links.TryClaimForSeriesAsync(tenantId, seriesId, connection!.Id, request.Platform, invitesPending: false, now, innerCt)
                    ?? throw new InvalidOperationException($"Series {seriesId} still holds a link after its release.");
                await _unitOfWork.SaveChangesAsync(innerCt);
                await _links.SetOccurrenceLinkAsync(tenantId, seriesId, linkId, now, _options.Value.Attendance.Checkpoints[0], innerCt);
                await QueueProvisioningAsync(tenantId, linkId, seriesId, generation: 1, innerCt);
                await _unitOfWork.SaveChangesAsync(innerCt);
                return Result<MeetingLinkResponse>.Success(
                    new MeetingLinkResponse(linkId, request.Platform, CalendarEventMeetingStatuses.Provisioning, null, null, null, 1));
            }, ct);
        }
        catch (MeetingBusyException)
        {
            return MeetingGuard.Busy<MeetingLinkResponse>();
        }
    }

    /// <summary>POST link/retry: the same generation again, only from provisioning_unknown (recovery: adopt what the first
    /// attempt may have created) or failed; the provider call runs in this request.</summary>
    public async Task<Result<MeetingLinkResponse>> RetryAsync(Guid tenantId, Guid callerEmployeeId, Guid seriesId, CancellationToken ct)
    {
        if (MeetingGuard.CheckWritable<MeetingLinkResponse>(_options.Value) is { } blocked)
            return blocked;
        if (await OrganizerOnlyAsync<MeetingLinkResponse>(tenantId, callerEmployeeId, seriesId, ct) is { } denied)
            return denied;
        var link = await SettleStaleAsync(tenantId, await _links.GetActiveForSeriesAsync(tenantId, seriesId, ct), ct);
        if (link is null)
            return Result<MeetingLinkResponse>.Failure("This meeting has no link to retry.", 409, MeetingErrorCodes.LinkNotRetryable);
        if (link.Status is CalendarEventMeetingStatuses.Provisioning or CalendarEventMeetingStatuses.Unresolved)
            return Unsettled<MeetingLinkResponse>(link.Status)!;
        if (link.Status is not (CalendarEventMeetingStatuses.ProvisioningUnknown or CalendarEventMeetingStatuses.Failed))
            return Result<MeetingLinkResponse>.Failure("This link cannot be retried.", 409, MeetingErrorCodes.LinkNotRetryable);
        var series = await _meetings.GetSeriesAsync(tenantId, seriesId, ct);
        if (series?.CancelledAt is not null)
            return Result<MeetingLinkResponse>.Failure("This meeting is cancelled.", 409, MeetingErrorCodes.MeetingCancelled);

        // Recovery must use the connection of the first attempt: createOrGet and the Zoom token are per organizer account.
        var connection = await _connections.GetByIdForTenantAsync(tenantId, link.ExternalCalendarConnectionId, ct);
        if (connection is null || !CalendarMeetingConnections.CanCreateMeetings(connection, link.Provider))
            return Result<MeetingLinkResponse>.Failure("Connect your calendar account again to retry the link.", 409,
                MeetingErrorCodes.ProviderNotConnected);
        if (!await _links.TryClaimRetryAsync(tenantId, link.Id, _clock.UtcNow, ct))
            return Unsettled<MeetingLinkResponse>(CalendarEventMeetingStatuses.Provisioning)!;
        return await ProvisionAsync(tenantId, link.Id, seriesId, recovery: link.Status == CalendarEventMeetingStatuses.ProvisioningUnknown, ct);
    }

    /// <summary>DELETE link: this series lets go of its link - its upcoming occurrences lose it, its attendees are told the
    /// link is gone, and the provider meeting is cancelled only if no other series or open attendance check still needs it
    /// (otherwise the response says <c>releasePending</c>). Null when the series had no link.</summary>
    public async Task<Result<MeetingLinkResponse?>> RemoveAsync(Guid tenantId, Guid callerEmployeeId, Guid seriesId, CancellationToken ct)
    {
        if (MeetingGuard.CheckWritable<MeetingLinkResponse?>(_options.Value) is { } blocked)
            return blocked;
        if (await OrganizerOnlyAsync<MeetingLinkResponse?>(tenantId, callerEmployeeId, seriesId, ct) is { } denied)
            return denied;
        await SettleStaleAsync(tenantId, await _links.GetActiveForSeriesAsync(tenantId, seriesId, ct), ct);

        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                await _writer.LockSeriesAsync(tenantId, seriesId, innerCt);
                var series = await _meetings.GetSeriesAsync(tenantId, seriesId, innerCt);
                if (series is null)
                    return MeetingReadService.NotFound<MeetingLinkResponse?>();
                var existing = await _links.GetActiveForSeriesAsync(tenantId, seriesId, innerCt);
                if (existing is null)
                    return Result<MeetingLinkResponse?>.Success(null);
                if (Unsettled<MeetingLinkResponse?>(existing.Status) is { } unsettled)
                    return unsettled;

                var now = _clock.UtcNow;
                await _links.SetOccurrenceLinkAsync(tenantId, seriesId, null, now, _options.Value.Attendance.Checkpoints[0], innerCt);
                var (link, parked) = await ReleaseReferenceAsync(tenantId, series, existing.Id, now, innerCt);
                // P1-4: nobody keeps a join link that no longer works for this meeting.
                if (existing.Status == CalendarEventMeetingStatuses.Active)
                    await _notifier.LinkRemovedAsync(series, link, innerCt);
                await _unitOfWork.SaveChangesAsync(innerCt);
                return Result<MeetingLinkResponse?>.Success(Describe(link, parked));
            }, ct);
        }
        catch (MeetingBusyException)
        {
            return MeetingGuard.Busy<MeetingLinkResponse?>();
        }
    }

    /// <summary>POST link/cleanup-confirmed: the organizer deleted the stray provider meeting; unresolved -> cancelled.</summary>
    public async Task<Result<bool>> ConfirmCleanupAsync(Guid tenantId, Guid callerEmployeeId, Guid seriesId, CancellationToken ct)
    {
        if (MeetingGuard.CheckWritable<bool>(_options.Value) is { } blocked)
            return blocked;
        if (await OrganizerOnlyAsync<bool>(tenantId, callerEmployeeId, seriesId, ct) is { } denied)
            return denied;
        var active = await _links.GetActiveForSeriesAsync(tenantId, seriesId, ct);
        var link = active is null ? null : await _links.GetTrackedAsync(tenantId, active.Id, ct);
        if (link is null || link.Status != CalendarEventMeetingStatuses.Unresolved)
            return Result<bool>.Failure("This link is not waiting for a cleanup.", 409, MeetingErrorCodes.LinkNotUnresolved);
        link.Status = CalendarEventMeetingStatuses.Cancelled;
        link.UpdatedAt = _clock.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    /// <summary>A cancelled or ended series lets go of its link reference, then the provider cancel decision (spec 5.7). Runs
    /// inside the caller's transaction, which holds the series lock and has saved the cancellations (the "needed" rule reads
    /// them); the caller saves. Null when the series held no reference.</summary>
    public async Task<MeetingLinkResponse?> ReleaseAsync(Guid tenantId, Guid seriesId, CancellationToken ct)
    {
        var active = await _links.GetActiveForSeriesAsync(tenantId, seriesId, ct);
        var series = active is null ? null : await _meetings.GetSeriesAsync(tenantId, seriesId, ct);
        if (active is null || series is null)
            return null;
        var (link, parked) = await ReleaseReferenceAsync(tenantId, series, active.Id, _clock.UtcNow, ct);
        return Describe(link, parked);
    }

    /// <summary>An active series link whose last need has ended (no referencing series with an upcoming slot, attendance
    /// finalized, the late-recheck window closed) is cancelled now and its provider cancel queued, under the link lock - a
    /// parked release, or the old half of a split whose other half was cancelled. True when it was cancelled.</summary>
    public Task<bool> CancelIfUnneededAsync(Guid tenantId, Guid linkId, CancellationToken ct)
        => _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await _links.LockLinkAsync(tenantId, linkId, innerCt);
            var link = await _links.GetTrackedAsync(tenantId, linkId, innerCt);
            var now = _clock.UtcNow;
            if (link is not { Status: CalendarEventMeetingStatuses.Active }
                || await IsNeededAsync(tenantId, linkId, now, innerCt)
                || await _links.GetLatestReferencingSeriesAsync(tenantId, linkId, innerCt) is not Guid seriesId
                || await _meetings.GetSeriesAsync(tenantId, seriesId, innerCt) is not { } series)
                return false;
            link.Status = CalendarEventMeetingStatuses.Cancelled;
            link.UpdatedAt = now;
            await QueueProviderCancelAsync(tenantId, link, series, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return true;
        }, ct);

    /// <summary>The spec 5.7 "needed" rule: an occurrence using the link that is upcoming, has an unfinalized attendance check
    /// or is inside its late-recheck window - or a series still referencing it with an upcoming (possibly virtual) slot.</summary>
    public async Task<bool> IsNeededAsync(Guid tenantId, Guid linkId, DateTimeOffset now, CancellationToken ct)
    {
        if (await _links.AnyOccurrenceNeedsLinkAsync(tenantId, linkId, now, _options.Value.Attendance.LateRecheckWindow, ct))
            return true;
        foreach (var seriesId in await _links.ListActiveReferencingSeriesAsync(tenantId, linkId, ct))
            if (await _meetings.GetSeriesAsync(tenantId, seriesId, ct) is { } series && HasUpcoming(series))
                return true;
        return false;
    }

    /// <summary>True while the series is not cancelled and its rule still has an occurrence after now.</summary>
    public bool HasUpcoming(MeetingSeries series)
    {
        var now = _clock.UtcNow;
        return series.CancelledAt is null
            && _recurrence.Occurrences(series.RecurrenceRule, series.DtStart, series.Timezone, now, now.AddDays(400)).Count > 0;
    }

    /// <summary>Under the link row lock: release the series' reference; when no reference is left and nothing needs the link,
    /// an active link is cancelled and its provider meeting cancel queued. Parked = the provider cancel waits (the link stays
    /// active for the other series or the attendance checks).</summary>
    private async Task<(CalendarEventMeeting Link, bool Parked)> ReleaseReferenceAsync(Guid tenantId, MeetingSeries series, Guid linkId,
        DateTimeOffset now, CancellationToken ct)
    {
        await _links.LockLinkAsync(tenantId, linkId, ct);
        await _links.ReleaseAsync(tenantId, series.Id, now, ct);
        var link = (await _links.GetTrackedAsync(tenantId, linkId, ct))!;
        if (link.Status != CalendarEventMeetingStatuses.Active)
            return (link, false);   // failed/cancelled: nothing exists remotely to cancel
        // IsNeededAsync covers every series still holding a reference (an upcoming slot keeps it), so a series that only
        // ended - e.g. the old half of a split - does not hold the provider meeting open.
        if (await IsNeededAsync(tenantId, linkId, now, ct))
            return (link, true);
        link.Status = CalendarEventMeetingStatuses.Cancelled;
        link.UpdatedAt = now;
        await QueueProviderCancelAsync(tenantId, link, series, ct);
        return (link, false);
    }

    /// <summary>409 for a link a write must not touch yet: provisioning, provisioning_unknown (retry first) or unresolved.</summary>
    private static Result<T>? Unsettled<T>(string status) => status switch
    {
        CalendarEventMeetingStatuses.Provisioning
            => Result<T>.Failure("The link is being created. Try again in a moment.", 409, MeetingErrorCodes.LinkInProgress),
        CalendarEventMeetingStatuses.ProvisioningUnknown
            => Result<T>.Failure("We could not confirm the link yet. Retry it first.", 409, MeetingErrorCodes.LinkStatusUnknown),
        CalendarEventMeetingStatuses.Unresolved
            => Result<T>.Failure("A provider meeting may still exist. Delete it there, then confirm.", 409, MeetingErrorCodes.LinkUnresolved),
        _ => null,
    };

    /// <summary>404 for anyone who cannot see the series (one body), 403 for an attendee, null for the organizer.</summary>
    private async Task<Result<T>?> OrganizerOnlyAsync<T>(Guid tenantId, Guid callerEmployeeId, Guid seriesId, CancellationToken ct)
    {
        var access = await _access.ResolveForSeriesAsync(tenantId, callerEmployeeId, seriesId, ct);
        if (access is null)
            return MeetingReadService.NotFound<T>();
        return access.Role == MeetingRoles.Organizer
            ? null
            : Result<T>.Failure("Only the organizer can change this meeting.", 403, MeetingErrorCodes.OrganizerRequired);
    }


    /// <summary>How long a claim may stay provisioning before its request or message is presumed dead (as legacy links).</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    /// <summary>A link still provisioning <see cref="StaleAfter"/> after its claim - the provision message died or was parked -
    /// becomes provisioning_unknown so Retry and Remove can settle it; the held-back invites go out (once) and the organizer
    /// is told. Returns the link as it now stands (null when it is gone).</summary>
    public async Task<CalendarEventMeeting?> RecoverStaleProvisioningAsync(Guid tenantId, Guid linkId, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        if (!await _links.TryMarkStaleProvisioningUnknownAsync(tenantId, linkId, now - StaleAfter, now, ct))
            return await _links.GetAsync(tenantId, linkId, ct);
        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var link = (await _links.GetTrackedAsync(tenantId, linkId, innerCt))!;
            var open = await OpenReferencingSeriesAsync(tenantId, linkId, innerCt);
            if (link.InvitesPending)
            {
                // Saved first: the notifier holds invites back while the stored link still says they are pending.
                link.InvitesPending = false;
                await _unitOfWork.SaveChangesAsync(innerCt);
                await SendHeldBackInvitesAsync(tenantId, open, innerCt);
            }
            if (open.FirstOrDefault() is { } series)
                await _notifier.NotifyOrganizerAsync(series, CalendarMeetingNoticeCodes.LinkUnknown, link.Provider, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return link;
        }, CancellationToken.None);
    }

    /// <summary>The snapshot after the stale-provisioning recovery, when it applies.</summary>
    private async Task<CalendarEventMeeting?> SettleStaleAsync(Guid tenantId, CalendarEventMeeting? link, CancellationToken ct)
        => link is { Status: CalendarEventMeetingStatuses.Provisioning, ProvisioningStartedAt: { } started } && started < _clock.UtcNow - StaleAfter
            ? await RecoverStaleProvisioningAsync(tenantId, link.Id, ct)
            : link;

    /// <summary>The non-cancelled series that still hold a reference to the link (after a split, more than one).</summary>
    private async Task<IReadOnlyList<MeetingSeries>> OpenReferencingSeriesAsync(Guid tenantId, Guid linkId, CancellationToken ct)
    {
        var ids = await _links.ListActiveReferencingSeriesAsync(tenantId, linkId, ct);
        return ids.Count == 0 ? [] : (await _meetings.ListSeriesAsync(tenantId, ids, ct)).Where(s => s.CancelledAt is null).ToList();
    }

    /// <summary>The invites held back while the link was being provisioned, to every open referencing series.</summary>
    private async Task SendHeldBackInvitesAsync(Guid tenantId, IReadOnlyList<MeetingSeries> open, CancellationToken ct)
    {
        foreach (var series in open)
            await _notifier.InviteAsync(series,
                (await _meetings.ListSeriesParticipantsAsync(tenantId, series.Id, ct)).Where(p => !p.IsDeleted).ToList(),
                await _meetings.ListSeriesGuestsAsync(tenantId, series.Id, ct), ct);
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
