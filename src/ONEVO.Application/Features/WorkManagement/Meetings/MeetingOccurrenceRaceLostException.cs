namespace ONEVO.Application.Features.WorkManagement.Meetings;

/// <summary>
/// This transaction inserted a meeting task but lost the race for the occurrence's meeting_details slot to a concurrent
/// writer (spec 5.6). The transaction can no longer commit - the deferred pairing trigger would reject the orphan task -
/// so roll it back and retry; the retry finds the slot taken and does nothing.
/// </summary>
public sealed class MeetingOccurrenceRaceLostException : Exception
{
    public MeetingOccurrenceRaceLostException(Guid seriesId, DateTimeOffset originalStart)
        : base($"Lost the race to materialize occurrence {seriesId}@{originalStart.ToUniversalTime():O}; roll back and retry.")
    {
        SeriesId = seriesId;
        OriginalStart = originalStart;
    }

    public Guid SeriesId { get; }
    public DateTimeOffset OriginalStart { get; }
}
