using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskStatusConfigurationTests
{
    [Fact]
    public void TaskStatus_DefaultsToRequiresApprovalFalseAndMarksTaskCompleteFalse()
    {
        var status = new TaskStatusEntity
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), ProjectId = Guid.NewGuid(),
            Name = "To Do", DisplayOrder = 0, CreatedAt = DateTimeOffset.UtcNow
        };

        Assert.False(status.RequiresApproval);
        Assert.False(status.MarksTaskComplete);
        Assert.Null(status.ObjectiveId);
        Assert.Null(status.ApproverId);
    }

    [Fact]
    public void TaskStatus_DefaultsVisibilityToPublic()
    {
        var status = new TaskStatusEntity { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Name = "Custom", CreatedAt = DateTimeOffset.UtcNow };

        Assert.Equal(TaskStatusVisibilities.Public, status.Visibility);
    }
}
