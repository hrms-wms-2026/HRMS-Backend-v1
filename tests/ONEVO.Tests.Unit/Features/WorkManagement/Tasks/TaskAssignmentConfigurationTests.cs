using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;
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

    [Fact]
    public void TaskAssignment_HasOneAssignmentPerTaskUniqueIndex()
    {
        var modelBuilder = new ModelBuilder();
        new TaskAssignmentConfiguration().Configure(modelBuilder.Entity<TaskAssignment>());

        var index = modelBuilder.Entity<TaskAssignment>().Metadata.GetIndexes()
            .Single(candidate => candidate.GetDatabaseName() == "ix_task_assignments_one_per_task");

        Assert.True(index.IsUnique);
        Assert.Equal(nameof(TaskAssignment.TaskId), Assert.Single(index.Properties).Name);
    }
}
