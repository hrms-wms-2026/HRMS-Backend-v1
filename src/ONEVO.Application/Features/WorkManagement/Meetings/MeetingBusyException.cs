namespace ONEVO.Application.Features.WorkManagement.Meetings;

/// <summary>The series lock was not granted within 5 s (spec 7 "meeting_busy"). The transaction is aborted: roll back
/// and answer 409 meeting_busy.</summary>
public sealed class MeetingBusyException : Exception
{
    public MeetingBusyException(Guid seriesId, Exception inner)
        : base($"Meeting series {seriesId} is locked by another change.", inner) => SeriesId = seriesId;

    public Guid SeriesId { get; }
}
