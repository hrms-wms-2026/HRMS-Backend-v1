using ONEVO.Domain.Features.Monitoring.WorkSessions.Entities;

namespace ONEVO.Application.Features.Monitoring.WorkSessions.RepositoryInterfaces;

public interface IWorkSessionRepository
{
    Task<EmployeeWorkSession?> FindByIdAsync(Guid id, Guid tenantId, CancellationToken ct);
    Task AddAsync(EmployeeWorkSession session, CancellationToken ct);

    /// <summary>
    /// Sessions whose ClockInAt falls within the UTC calendar day [date, date+1).
    /// A day may have more than one session if the employee clocked in/out
    /// multiple times.
    /// </summary>
    Task<IReadOnlyList<EmployeeWorkSession>> GetForUserAndDateAsync(
        Guid tenantId, Guid userId, DateOnly date, CancellationToken ct);
}
