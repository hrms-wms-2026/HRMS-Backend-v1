using FluentAssertions;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteOffboarding;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class OffboardingCompletionGateTests
{
    private static EmployeeChecklistTask Task(bool required, string status) =>
        new() { Id = Guid.NewGuid(), IsRequired = required, Status = status };

    [Fact]
    public void AllRequiredTasksResolved_AllRequiredCompleted_ReturnsTrue()
    {
        var tasks = new[] { Task(true, EmployeeChecklistTaskStatuses.Completed), Task(true, EmployeeChecklistTaskStatuses.Bypassed) };
        OffboardingCompletionGate.AllRequiredTasksResolved(tasks).Should().BeTrue();
    }

    [Fact]
    public void AllRequiredTasksResolved_OneRequiredStillPending_ReturnsFalse()
    {
        var tasks = new[] { Task(true, EmployeeChecklistTaskStatuses.Completed), Task(true, EmployeeChecklistTaskStatuses.Pending) };
        OffboardingCompletionGate.AllRequiredTasksResolved(tasks).Should().BeFalse();
    }

    [Fact]
    public void AllRequiredTasksResolved_NonRequiredStillPending_DoesNotBlock()
    {
        var tasks = new[] { Task(true, EmployeeChecklistTaskStatuses.Completed), Task(false, EmployeeChecklistTaskStatuses.Pending) };
        OffboardingCompletionGate.AllRequiredTasksResolved(tasks).Should().BeTrue();
    }

    [Fact]
    public void AllRequiredTasksResolved_NoTasksAtAll_ReturnsTrue()
    {
        OffboardingCompletionGate.AllRequiredTasksResolved(Array.Empty<EmployeeChecklistTask>()).Should().BeTrue();
    }
}
