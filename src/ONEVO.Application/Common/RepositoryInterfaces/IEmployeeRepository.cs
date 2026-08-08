using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Common.RepositoryInterfaces;

public interface IEmployeeRepository
{
    Task<Employee?> GetByUserIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>Batch lookup for name resolution - every Employee row for the given UserIds, in one
    /// query. Used instead of N individual GetByUserIdAsync calls when resolving display names for a
    /// list (e.g. Owner/Reporting-Manager names across every milestone in a project).</summary>
    Task<IReadOnlyList<Employee>> GetByUserIdsAsync(Guid tenantId, IReadOnlyList<Guid> userIds, CancellationToken ct = default);
}
