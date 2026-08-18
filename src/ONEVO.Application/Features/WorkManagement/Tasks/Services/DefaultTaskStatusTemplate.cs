using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public static class DefaultTaskStatusTemplate
{
    public static List<TaskStatusEntity> BuildRows(
        Guid tenantId, Guid projectId, Guid? objectiveId, Guid createdById, DateTimeOffset now)
    {
        return new List<TaskStatusEntity>
        {
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ObjectiveId = objectiveId, Name = "To Do", DisplayOrder = 0, Visibility = TaskStatusVisibilities.Public, CreatedById = createdById, CreatedAt = now },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ObjectiveId = objectiveId, Name = "In Process", DisplayOrder = 1, Visibility = TaskStatusVisibilities.Public, CreatedById = createdById, CreatedAt = now },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ObjectiveId = objectiveId, Name = "Review", DisplayOrder = 2, Visibility = TaskStatusVisibilities.Public, CreatedById = createdById, CreatedAt = now },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ObjectiveId = objectiveId, Name = "Done", DisplayOrder = 3, MarksTaskComplete = true, Visibility = TaskStatusVisibilities.Private, CreatedById = createdById, CreatedAt = now }
        };
    }
}
