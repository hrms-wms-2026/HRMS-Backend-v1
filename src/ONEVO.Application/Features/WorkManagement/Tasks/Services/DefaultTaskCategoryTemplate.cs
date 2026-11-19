using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public static class DefaultTaskCategoryTemplate
{
    public static List<TaskCategory> BuildRows(Guid tenantId, Guid projectId, Guid createdById, DateTimeOffset now)
    {
        return new List<TaskCategory>
        {
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Name = "task", Kind = TaskCategoryKinds.Task, DisplayOrder = 0, CreatedById = createdById, CreatedAt = now },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Name = "bug", Kind = TaskCategoryKinds.Bug, DisplayOrder = 1, CreatedById = createdById, CreatedAt = now },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Name = "story", Kind = TaskCategoryKinds.Story, DisplayOrder = 2, CreatedById = createdById, CreatedAt = now },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Name = "feature", Kind = TaskCategoryKinds.Feature, DisplayOrder = 3, CreatedById = createdById, CreatedAt = now },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Name = TaskCategorySystemKeys.Meeting, DisplayOrder = 4, SystemKey = TaskCategorySystemKeys.Meeting, Kind = TaskCategoryKinds.Meeting, CreatedById = createdById, CreatedAt = now }
        };
    }
}
