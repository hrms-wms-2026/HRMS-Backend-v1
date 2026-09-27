using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;

public interface ISprintActivityLogRepository
{
    Task AddAsync(SprintActivityLog log, CancellationToken ct = default);
    Task<IReadOnlyList<SprintActivityLog>> GetForSprintAsync(Guid tenantId, Guid sprintId, CancellationToken ct = default);
}
