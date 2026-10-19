using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;

public interface ILeaveApprovalRepository
{
    Task<LeaveApprovalState?> GetStateAsync(Guid tenantId, Guid requestId, CancellationToken ct = default);

    Task<IReadOnlyList<LeavePendingApprovalListRow>> ListPendingForApproverAsync(
        Guid tenantId,
        Guid approverEmployeeId,
        LeaveApprovalListFilter filter,
        CancellationToken ct = default);

    Task<IReadOnlyList<LeaveRequestAllListRow>> ListAllAsync(
        Guid tenantId,
        LeaveRequestAllListFilter filter,
        CancellationToken ct = default);


    Task AddApproverAsync(LeaveRequestApprover approver, CancellationToken ct = default);

    /// <summary>Requests this approver already acted on (approved, rejected, forwarded or asked for info),
    /// newest action first.</summary>
    Task<IReadOnlyList<LeaveApprovalHistoryRow>> ListApprovalHistoryAsync(
        Guid tenantId,
        Guid approverEmployeeId,
        DateOnly? fromDate,
        DateOnly? toDate,
        int limit,
        CancellationToken ct = default);

    Task AddBalanceAuditAsync(LeaveBalanceAudit audit, CancellationToken ct = default);

    Task AddDocumentsAsync(IReadOnlyCollection<LeaveRequestDocument> documents, CancellationToken ct = default);

    Task<bool> AreAvailableFileRecordsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileRecordIds,
        CancellationToken ct = default);

    Task<int> CountDocumentsAsync(Guid tenantId, Guid requestId, CancellationToken ct = default);

    /// <summary>Requests a user submitted on behalf of someone else, newest first.</summary>
    Task<IReadOnlyList<LeaveRequest>> ListSubmittedOnBehalfAsync(Guid tenantId, Guid submitterUserId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, string>> ListLeaveTypeNamesAsync(Guid tenantId, IReadOnlyCollection<Guid> leaveTypeIds, CancellationToken ct = default);

    /// <summary>Removes a pending approver row (undoing a forward). Saved with SaveChangesAsync.</summary>
    void RemoveApprover(LeaveRequestApprover approver);

    /// <summary>The request's active day allocations, tracked (edited approval replaces them).</summary>
    Task<IReadOnlyList<LeaveRequestDayAllocation>> ListActiveAllocationsAsync(Guid tenantId, Guid requestId, CancellationToken ct = default);

    Task AddAllocationsAsync(IReadOnlyCollection<LeaveRequestDayAllocation> allocations, CancellationToken ct = default);

    /// <summary>Balance audit rows of this employee and leave type created after <paramref name="since"/>, oldest first
    /// (what changed the balance after a decision - the Inbox revert checks it).</summary>
    Task<IReadOnlyList<LeaveBalanceAudit>> ListBalanceAuditsSinceAsync(
        Guid tenantId, Guid employeeId, Guid leaveTypeId, DateTimeOffset since, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed record LeaveApprovalState(
    LeaveRequest Request,
    LeaveEntitlement? Entitlement,
    Employee Employee,
    string LeaveTypeName,
    string LeaveTypeCode,
    string? ApprovalMode,
    IReadOnlyList<LeaveRequestApprover> Approvers);

public sealed record LeaveApprovalListFilter(
    string? Search,
    Guid? DepartmentId,
    Guid? LeaveTypeId,
    DateOnly? FromDate,
    DateOnly? ToDate,
    Guid? LegalEntityId = null);

public sealed record LeaveRequestAllListFilter(
    string? Search,
    Guid? DepartmentId,
    Guid? LeaveTypeId,
    string? Status,
    DateOnly? FromDate,
    DateOnly? ToDate);

public sealed record LeavePendingApprovalListRow(
    LeaveRequest Request,
    string EmployeeName,
    string LeaveTypeName,
    string LeaveTypeCode);

public sealed record LeaveRequestAllListRow(
    LeaveRequest Request,
    string EmployeeName,
    Guid? DepartmentId,
    string? DepartmentName,
    string LeaveTypeName,
    IReadOnlyList<string> ApproverNames);

public sealed record LeaveApprovalHistoryRow(
    LeaveRequest Request,
    string EmployeeName,
    string LeaveTypeName,
    string MyAction,
    DateTimeOffset ActedAt,
    string? MyComment);
