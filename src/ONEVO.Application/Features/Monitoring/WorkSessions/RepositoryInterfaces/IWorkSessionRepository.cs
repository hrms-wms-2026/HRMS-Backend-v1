using ONEVO.Domain.Features.Monitoring.WorkSessions.Entities;

namespace ONEVO.Application.Features.Monitoring.WorkSessions.RepositoryInterfaces;

public interface IWorkSessionRepository
{
    Task<EmployeeWorkSession?> FindByIdAsync(Guid id, Guid tenantId, CancellationToken ct);
    Task AddAsync(EmployeeWorkSession session, CancellationToken ct);
}
