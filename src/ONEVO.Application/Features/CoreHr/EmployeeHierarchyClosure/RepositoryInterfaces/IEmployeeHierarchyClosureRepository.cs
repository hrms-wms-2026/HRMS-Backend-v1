namespace ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;

public interface IEmployeeHierarchyClosureRepository
{
    /// <summary>
    /// Rebuilds the entire tenant's closure rows from current active PrimaryEmployment
    /// position_assignments and positions.reports_to_position_id. This table is not source
    /// of truth and is safe to delete and rebuild in full per tenant.
    /// </summary>
    Task RebuildAsync(Guid tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetDirectReportEmployeeIdsAsync(
        Guid tenantId, Guid managerEmployeeId, CancellationToken ct = default);

    Task<Guid?> GetDirectManagerEmployeeIdAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default);
}
