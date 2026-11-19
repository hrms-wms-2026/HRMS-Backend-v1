using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskDueDateEventWindowRuleTests
{
    private static readonly IReadOnlyList<ActiveEventWindow> Windows = new[]
    {
        new ActiveEventWindow(Guid.NewGuid(), "Launch", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10))
    };

    [Fact]
    public void NoWindows_AllowsAnything() =>
        Assert.Null(TaskDueDateEventWindowRule.Validate(Array.Empty<ActiveEventWindow>(), null));

    [Fact]
    public void InsideWindow_Allowed() =>
        Assert.Null(TaskDueDateEventWindowRule.Validate(Windows, new DateOnly(2026, 10, 5)));

    [Fact]
    public void ClearingDueDate_InActiveEvent_IsRejected() =>
        Assert.Equal("This task is in active event(s) Launch; a due date is required.",
            TaskDueDateEventWindowRule.Validate(Windows, null));

    [Fact]
    public void OutsideWindow_IsRejectedWithWindowDetails() =>
        Assert.Equal(
            "Due date 2026-11-01 is outside event window(s): Launch 2026-10-01..2026-10-10. Widen the event first.",
            TaskDueDateEventWindowRule.Validate(Windows, new DateOnly(2026, 11, 1)));
}
