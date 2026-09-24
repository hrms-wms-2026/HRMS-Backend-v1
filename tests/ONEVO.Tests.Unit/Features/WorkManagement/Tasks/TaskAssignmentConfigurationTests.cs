using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskAssignmentConfigurationTests
{
    [Fact]
    public void TaskAssignment_CanBeConstructedWithRequiredFields()
    {
        var assignment = new TaskAssignment
        {
            Id = Guid.NewGuid(), TaskId = Guid.NewGuid(), UserId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(), AssignedById = Guid.NewGuid(), AssignedAt = DateTimeOffset.UtcNow
        };

        Assert.NotEqual(Guid.Empty, assignment.TaskId);
        Assert.NotEqual(Guid.Empty, assignment.EmployeeId);
    }
}
