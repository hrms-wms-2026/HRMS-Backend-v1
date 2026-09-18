using ONEVO.Infrastructure.Services.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class SprintLifecycleJobTests
{
    [Fact]
    public void ShouldNotifyOverdue_PastEndDateWithUnfinishedTasksNotYetNotified_ReturnsTrue()
    {
        var result = SprintLifecycleJob.ShouldNotifyOverdue(
            endDate: new DateOnly(2026, 9, 14), today: new DateOnly(2026, 9, 15),
            allTasksComplete: false, alreadyNotified: false);

        Assert.True(result);
    }

    [Fact]
    public void ShouldNotifyOverdue_EndDateNotYetPassed_ReturnsFalse()
    {
        var result = SprintLifecycleJob.ShouldNotifyOverdue(
            endDate: new DateOnly(2026, 9, 14), today: new DateOnly(2026, 9, 10),
            allTasksComplete: false, alreadyNotified: false);

        Assert.False(result);
    }

    [Fact]
    public void ShouldNotifyOverdue_AllTasksComplete_ReturnsFalse()
    {
        var result = SprintLifecycleJob.ShouldNotifyOverdue(
            endDate: new DateOnly(2026, 9, 14), today: new DateOnly(2026, 9, 15),
            allTasksComplete: true, alreadyNotified: false);

        Assert.False(result);
    }

    [Fact]
    public void ShouldNotifyOverdue_AlreadyNotified_ReturnsFalse()
    {
        var result = SprintLifecycleJob.ShouldNotifyOverdue(
            endDate: new DateOnly(2026, 9, 14), today: new DateOnly(2026, 9, 20),
            allTasksComplete: false, alreadyNotified: true);

        Assert.False(result);
    }
}
