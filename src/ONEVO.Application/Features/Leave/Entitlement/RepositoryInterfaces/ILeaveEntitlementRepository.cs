using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;

namespace ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;

public interface ILeaveEntitlementRepository
{
    Task<IReadOnlyList<LeaveEntitlementRow>> ListRowsAsync(
        Guid tenantId,
        LeaveEntitlementListFilter filter,
        CancellationToken ct = default);

    Task<LeaveEntitlementRow?> GetRowByIdAsync(Guid tenantId, Guid entitlementId, CancellationToken ct = default);

    Task<IReadOnlyList<LeaveEntitlement>> ListExistingAsync(
        Guid tenantId,
        int year,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct = default);

    Task<LeaveEntitlement?> GetTrackedByIdAsync(Guid tenantId, Guid entitlementId, CancellationToken ct = default);

    Task<LeaveEntitlement?> GetTrackedByEmployeeTypeYearAsync(
        Guid tenantId,
        Guid employeeId,
        Guid leaveTypeId,
        int year,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<(Guid EmployeeId, Guid LeaveTypeId), LeaveEntitlement>> ListPreviousYearAsync(
        Guid tenantId,
        int previousYear,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct = default);

    Task AddGeneratedAsync(IReadOnlyCollection<LeaveEntitlementWriteSet> writeSets, CancellationToken ct = default);

    Task AddManualAsync(LeaveEntitlement entitlement, LeaveBalanceAudit audit, CancellationToken ct = default);

    Task SaveWithAuditAsync(LeaveEntitlement entitlement, LeaveBalanceAudit audit, CancellationToken ct = default);
}

public record LeaveEntitlementListFilter(
    int Year,
    Guid? EmployeeId,
    IReadOnlyCollection<Guid>? EmployeeIds,
    Guid? LegalEntityId,
    Guid? DepartmentId,
    Guid? LeaveTypeId,
    int? EmploymentStatusId,
    string? Search);

public record LeaveEntitlementWriteSet(
    LeaveEntitlement Entitlement,
    IReadOnlyList<LeaveBalanceAudit> Audits);

public record LeaveEntitlementRow(
    LeaveEntitlement Entitlement,
    string EmployeeNumber,
    string EmployeeName,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LegalEntityId,
    string? LegalEntityName,
    string LeaveTypeName,
    string LeaveTypeCode,
    decimal RemainingDays);
