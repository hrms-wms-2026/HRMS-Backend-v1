using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Common.RepositoryInterfaces;

public interface IEmployeeRepository
{
    Task<Employee?> GetByUserIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>Batch lookup for name resolution - every Employee row for the given UserIds, in one
    /// query. Used instead of N individual GetByUserIdAsync calls when resolving display names for a
    /// list (e.g. Owner/Reporting-Manager names across every milestone in a project).</summary>
    Task<IReadOnlyList<Employee>> GetByUserIdsAsync(Guid tenantId, IReadOnlyList<Guid> userIds, CancellationToken ct = default);

    /// <summary>Looks the Employee up by its own Id (not by UserId) - used by
    /// IMilestoneMembershipCoordinator and other EmployeeId-keyed Work Management callers.</summary>
    Task<Employee?> GetByIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, Employee>> ListByIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);

    /// <summary>Every employee in the given departments, in ONE query - the department's own employees only,
    /// not its sub-departments'. Used by the meetings bulk invite preview to expand a picked department
    /// (spec 5.2, D8). No employment-status filter: the caller decides who counts as active.</summary>
    Task<IReadOnlyList<Employee>> ListByDepartmentIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> departmentIds, CancellationToken ct = default);

    /// <summary>The manager's direct reports - the employees whose reporting line points straight at
    /// them. Used by the meetings bulk invite preview to expand "a manager's team" (spec 5.2, D8).
    /// The reporting-line closure is read here, and not through
    /// IEmployeeHierarchyClosureRepository: WorkManagement may never reference the raw coverage
    /// tables (My Team spec section 10), so the read is exposed on the common interface instead.</summary>
    Task<IReadOnlyList<Guid>> ListDirectReportEmployeeIdsAsync(
        Guid tenantId, Guid managerEmployeeId, CancellationToken ct = default);

    Task<IReadOnlyList<Employee>> ListActiveByLegalEntityAsync(
        Guid tenantId,
        Guid? legalEntityId,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, string>> ListLegalEntityChangeWarningsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        int year,
        CancellationToken ct = default);
}
