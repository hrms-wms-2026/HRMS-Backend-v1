using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class WorkTaskConfigurationTests
{
    [Fact]
    public void WorkTask_DefaultsToZeroProgressAndZeroCompletedHours()
    {
        var task = new WorkTask
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), ProjectId = Guid.NewGuid(),
            ObjectiveId = Guid.NewGuid(), ShortId = "PRJ-1", Title = "Do the thing",
            CategoryId = Guid.NewGuid(), StatusId = Guid.NewGuid(), Priority = WorkTaskPriorities.Medium,
            CreatedById = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
        };

        Assert.Equal(0, task.ProgressPercent);
        Assert.Equal(0m, task.CompletedHours);
        Assert.Null(task.ParentTaskId);
        Assert.Null(task.CompletedAt);
    }
}
