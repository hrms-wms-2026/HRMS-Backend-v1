using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public static class DefaultTaskCategoryTemplate
{
    public static List<TaskCategory> BuildRows(Guid tenantId, Guid projectId, Guid createdById, DateTimeOffset now)
    {
        return new List<TaskCategory>
        {
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Name = "task", DisplayOrder = 0, CreatedById = createdById, CreatedAt = now },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Name = "bug", DisplayOrder = 1, CreatedById = createdById, CreatedAt = now },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Name = "story", DisplayOrder = 2, CreatedById = createdById, CreatedAt = now },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Name = "feature", DisplayOrder = 3, CreatedById = createdById, CreatedAt = now }
        };
    }
}
