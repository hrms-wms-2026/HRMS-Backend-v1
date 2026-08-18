using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskStatusRepository
{
    Task AddAsync(TaskStatusEntity status, CancellationToken ct = default);
    Task AddRangeAsync(IReadOnlyList<TaskStatusEntity> statuses, CancellationToken ct = default);

    /// <summary>Project-level template rows (ObjectiveId == null), ordered by DisplayOrder.</summary>
    Task<IReadOnlyList<TaskStatusEntity>> GetProjectTemplateAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);

    /// <summary>An Objective's own status rows (ObjectiveId == the given id), ordered by DisplayOrder. Empty if not yet copied from the template.</summary>
    Task<IReadOnlyList<TaskStatusEntity>> GetByObjectiveIdAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default);

    Task<TaskStatusEntity?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    void Update(TaskStatusEntity status);
    void Remove(TaskStatusEntity status);
}
