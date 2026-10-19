using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>A meeting's status is derived from time and cancellation, never stored or set by hand (spec D10).</summary>
public static class MeetingStatusCalculator
{
    public static string Of(MeetingDetails occurrence, MeetingSeries series, DateTimeOffset now)
    {
        if (occurrence.CancelledAt is not null || series.CancelledAt is not null)
            return MeetingStatuses.Cancelled;
        if (now >= occurrence.EndAt)
            return MeetingStatuses.Completed;
        return now >= occurrence.StartAt ? MeetingStatuses.InProgress : MeetingStatuses.Scheduled;
    }
}
