namespace ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;

/// <summary>Everything one materialized occurrence needs: its meeting task and its meeting_details row. The task id is
/// not part of it - the writer derives it (<see cref="MeetingOccurrenceIds"/>).</summary>
public sealed record MeetingOccurrenceDraft(
    Guid TenantId, Guid SeriesId, Guid ProjectId, Guid ObjectiveId, Guid CategoryId, Guid StatusId,
    string ShortId, string Title, string? Description, Guid CreatedById,
    DateTimeOffset OriginalStart, DateTimeOffset StartAt, DateTimeOffset EndAt, bool IsAllDay,
    string? Location, bool IsPrivate, Guid? CalendarEventMeetingId);

/// <summary>
/// The concurrency-safe occurrence writes of meetings-as-tasks Phase 2 (spec 5.6). Every method needs an ambient
/// transaction. Uniqueness never depends on the series lock, but correctness does: a generator must
/// <b>lock the series, then re-read it (rule, UNTIL, cancellation), then write</b> - otherwise a split that commits
/// between its read and its write lets it materialize a slot the series no longer has.
/// </summary>
public interface IMeetingOccurrenceWriter
{
    /// <summary>Takes the series' transaction-scoped advisory lock; call inside a transaction.</summary>
    Task LockSeriesAsync(Guid tenantId, Guid seriesId, CancellationToken ct);

    /// <summary>Inserts the occurrence unless (series, original_start) already exists or is suppressed. Returns the task
    /// id when it inserted, null when it did nothing. Throws <see cref="MeetingOccurrenceRaceLostException"/> when an
    /// unlocked concurrent writer won the slot: roll back and retry.</summary>
    Task<Guid?> TryMaterializeAsync(MeetingOccurrenceDraft draft, CancellationToken ct);

    /// <summary>Moves occurrences into an EMPTY target series in one statement (split / move), keeping their task ids.
    /// The new starts must be strictly increasing in mapping order. Takes the target's series lock itself; the caller
    /// holds the source series' lock.</summary>
    Task RemapIntoSeriesAsync(Guid tenantId, Guid targetSeriesId,
        IReadOnlyList<(Guid TaskId, DateTimeOffset NewOriginalStart, DateTimeOffset NewStartAt, DateTimeOffset NewEndAt)> mapping,
        CancellationToken ct);
}
