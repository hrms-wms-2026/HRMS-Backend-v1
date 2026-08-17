using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Infrastructure.Services.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class SprintLifecycleJobTests
{
    [Fact]
    public void DetermineNextStatus_FutureSprintStartDateReached_ReturnsActive()
    {
        var today = new DateOnly(2026, 9, 1);
        var next = SprintLifecycleJob.DetermineNextStatus(SprintStatuses.Future, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), today, allTasksComplete: false);

        Assert.Equal(SprintStatuses.Active, next);
    }

    [Fact]
    public void DetermineNextStatus_FutureSprintStartDateNotYetReached_StaysFuture()
    {
        var today = new DateOnly(2026, 8, 30);
        var next = SprintLifecycleJob.DetermineNextStatus(SprintStatuses.Future, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), today, allTasksComplete: false);

        Assert.Null(next);
    }

    [Fact]
    public void DetermineNextStatus_ActiveSprintPastEndDateWithUnfinishedTasks_ReturnsIncomplete()
    {
        var today = new DateOnly(2026, 9, 15);
        var next = SprintLifecycleJob.DetermineNextStatus(SprintStatuses.Active, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), today, allTasksComplete: false);

        Assert.Equal(SprintStatuses.Incomplete, next);
    }

    [Fact]
    public void DetermineNextStatus_ActiveSprintPastEndDateAllTasksComplete_StaysActive()
    {
        // Completion is a manual owner action (CompleteSprintCommand) - the job never auto-completes,
        // it only auto-flags Incomplete. An owner who hasn't clicked Complete yet keeps the sprint Active.
        var today = new DateOnly(2026, 9, 15);
        var next = SprintLifecycleJob.DetermineNextStatus(SprintStatuses.Active, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), today, allTasksComplete: true);

        Assert.Null(next);
    }

    [Fact]
    public void DetermineNextStatus_TerminalStatuses_NeverChange()
    {
        Assert.Null(SprintLifecycleJob.DetermineNextStatus(SprintStatuses.Complete, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 20), allTasksComplete: true));
        Assert.Null(SprintLifecycleJob.DetermineNextStatus(SprintStatuses.Incomplete, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 20), allTasksComplete: false));
        Assert.Null(SprintLifecycleJob.DetermineNextStatus(SprintStatuses.Achieved, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 20), allTasksComplete: true));
    }
}
