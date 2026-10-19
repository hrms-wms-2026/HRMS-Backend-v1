using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>A meeting's status is derived from time and cancellation, never stored or set by hand (spec D10).</summary>
public static class MeetingStatusCalculator
{
    public static string Of(MeetingDetails occurrence, MeetingSeries series, DateTimeOffset now)
    {
        if (occurrence.CancelledAt is not null || CancelledWithSeries(occurrence, series))
            return MeetingStatuses.Cancelled;
        if (now >= occurrence.EndAt)
            return MeetingStatuses.Completed;
        return now >= occurrence.StartAt ? MeetingStatuses.InProgress : MeetingStatuses.Scheduled;
    }

    /// <summary>A series cancellation covers only the occurrences that had not started when it happened: the meetings that
    /// took place stay as they were (spec 4.2, D19).</summary>
    public static bool CancelledWithSeries(MeetingDetails occurrence, MeetingSeries series)
        => series.CancelledAt is { } cancelledAt && occurrence.StartAt >= cancelledAt;
}
