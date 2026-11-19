using ONEVO.Api.Contracts.Calendar;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using Xunit;

namespace ONEVO.Tests.Unit.Api.Calendar;

/// <summary>GET /calendar view model: a task meeting merged into the feed (meetings Phase 2, Plan C) must keep what the
/// client needs to open its drawer and show its status.</summary>
public class CalendarEventViewModelMapperTests
{
    [Fact]
    public void A_task_meeting_keeps_its_kind_series_task_and_status()
    {
        var seriesId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var item = new CalendarEventItem(Guid.NewGuid(), "Weekly sync", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            "meeting", null, "none", false, "Asia/Colombo", "confirmed", false, null, null, null, Guid.NewGuid(),
            ItemKind: "meeting", MeetingSeriesId: seriesId, MeetingTaskId: taskId, MeetingStatus: "scheduled");

        var view = item.ToViewModel();

        Assert.Equal(("meeting", (Guid?)seriesId, (Guid?)taskId, "scheduled"),
            (view.ItemKind, view.MeetingSeriesId, view.MeetingTaskId, view.MeetingStatus));
    }

    [Fact]
    public void A_plain_event_has_no_meeting_fields()
    {
        var item = new CalendarEventItem(Guid.NewGuid(), "Lunch", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            "manual", null, "none", false, null, "confirmed", false, null, null, null, Guid.NewGuid());

        var view = item.ToViewModel();

        Assert.Equal(((string?)null, (Guid?)null, (Guid?)null, (string?)null),
            (view.ItemKind, view.MeetingSeriesId, view.MeetingTaskId, view.MeetingStatus));
    }
}
