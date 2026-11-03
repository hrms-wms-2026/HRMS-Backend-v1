using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;

/// <summary>
/// Teams/Zoom links owned by meeting series (spec 4.1, 5.7): a <c>calendar_event_meetings</c> row with
/// <c>owner_kind = meeting_series</c>, referenced through <c>meeting_series_links</c>. A split series inherits a reference
/// to its parent's link, so one link can have several references; it is released only when the last one is and the link is
/// no longer needed. The conditional status updates by link id (new generation, stale, unknown, unresolved) are the legacy
/// <see cref="ONEVO.Application.Features.Calendar.RepositoryInterfaces.ICalendarEventMeetingRepository"/> ones. Never calls
/// SaveChangesAsync.
/// </summary>
public interface IMeetingLinkRepository
{
    /// <summary>The link behind the series' active (unreleased) reference, untracked; null when it has none.</summary>
    Task<CalendarEventMeeting?> GetActiveForSeriesAsync(Guid tenantId, Guid seriesId, CancellationToken ct = default);

    Task<CalendarEventMeeting?> GetAsync(Guid tenantId, Guid linkId, CancellationToken ct = default);

    Task<CalendarEventMeeting?> GetTrackedAsync(Guid tenantId, Guid linkId, CancellationToken ct = default);

    /// <summary>Stages a provisioning link row (generation 1) and the series' <c>created</c> reference. The caller holds the
    /// series lock (so two claims cannot interleave) and saves; null when the series already has an active reference.</summary>
    Task<Guid?> TryClaimForSeriesAsync(Guid tenantId, Guid seriesId, Guid connectionId, string provider, bool invitesPending,
        DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Stages an <c>inherited_on_split</c> reference of <paramref name="toSeriesId"/> to the link
    /// <paramref name="fromSeriesId"/> holds; returns that link id, or null (nothing staged) when it holds none.</summary>
    Task<Guid?> AddInheritedReferenceAsync(Guid tenantId, Guid fromSeriesId, Guid toSeriesId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Releases the series' active reference at once (bulk update); the released link id, or null when it held none.</summary>
    Task<Guid?> ReleaseAsync(Guid tenantId, Guid seriesId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>The series that still hold an unreleased reference to the link.</summary>
    Task<IReadOnlyList<Guid>> ListActiveReferencingSeriesAsync(Guid tenantId, Guid linkId, CancellationToken ct = default);

    /// <summary>True while any occurrence using the link is upcoming and not cancelled, has an attendance check that is not
    /// finalized, or is still inside its late-recheck window (the materialized half of the spec 5.7 "needed" rule).</summary>
    Task<bool> AnyOccurrenceNeedsLinkAsync(Guid tenantId, Guid linkId, DateTimeOffset now, TimeSpan lateRecheckWindow, CancellationToken ct = default);

    /// <summary>Points the series' occurrences starting at or after <paramref name="fromStart"/> at the link (or at none) and
    /// restarts their attendance: pending at <paramref name="firstCheckpoint"/> after their end, or not_applicable without a
    /// link. Runs at once (bulk update).</summary>
    Task SetOccurrenceLinkAsync(Guid tenantId, Guid seriesId, Guid? linkId, DateTimeOffset fromStart, TimeSpan firstCheckpoint,
        CancellationToken ct = default);

    /// <summary>provisioning claimed before <paramref name="staleBefore"/> -> provisioning_unknown (its request or message
    /// died); runs at once. False when it was no longer such a row.</summary>
    Task<bool> TryMarkStaleProvisioningUnknownAsync(Guid tenantId, Guid linkId, DateTimeOffset staleBefore, DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>Series-owned links still provisioning since before <paramref name="staleBefore"/>.</summary>
    Task<IReadOnlyList<Guid>> ListStaleProvisioningLinkIdsAsync(Guid tenantId, DateTimeOffset staleBefore, CancellationToken ct = default);

    /// <summary>Retry: provisioning_unknown or failed -> provisioning, same generation (runs at once). False = the link was
    /// in neither state any more (someone else claimed it).</summary>
    Task<bool> TryClaimRetryAsync(Guid tenantId, Guid linkId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Active series-owned links that have a reference: the candidates the release check (spec 5.7 "needed" rule)
    /// looks at on every attendance-job run.</summary>
    Task<IReadOnlyList<Guid>> ListReleaseCandidateLinkIdsAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>The series that referenced the link last (an unreleased reference first, else the latest released one).</summary>
    Task<Guid?> GetLatestReferencingSeriesAsync(Guid tenantId, Guid linkId, CancellationToken ct = default);

    /// <summary>SELECT ... FOR UPDATE on the link row; the release decision runs under it. Needs a transaction.</summary>
    Task LockLinkAsync(Guid tenantId, Guid linkId, CancellationToken ct = default);
}
