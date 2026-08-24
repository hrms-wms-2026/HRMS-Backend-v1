using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskCategoryRepository
{
    Task AddAsync(TaskCategory category, CancellationToken ct = default);
    Task AddRangeAsync(IReadOnlyList<TaskCategory> categories, CancellationToken ct = default);
    Task<IReadOnlyList<TaskCategory>> GetByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);
    Task<TaskCategory?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    void Update(TaskCategory category);
    void Remove(TaskCategory category);
}
