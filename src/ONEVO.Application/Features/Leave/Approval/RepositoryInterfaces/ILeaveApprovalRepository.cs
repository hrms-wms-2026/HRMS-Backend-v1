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

    Task AddInfoMessageAsync(LeaveRequestInfoMessage message, CancellationToken ct = default);

    Task AddBalanceAuditAsync(LeaveBalanceAudit audit, CancellationToken ct = default);

    Task AddDocumentsAsync(IReadOnlyCollection<LeaveRequestDocument> documents, CancellationToken ct = default);

    Task<bool> AreAvailableFileRecordsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileRecordIds,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed record LeaveApprovalState(
    LeaveRequest Request,
    LeaveEntitlement? Entitlement,
    Employee Employee,
    string LeaveTypeName,
    string LeaveTypeCode,
    string? ApprovalMode,
    IReadOnlyList<LeaveRequestApprover> Approvers,
    IReadOnlyList<LeaveRequestInfoMessage> InfoMessages);

public sealed record LeaveApprovalListFilter(
    string? Search,
    Guid? DepartmentId,
    Guid? LeaveTypeId,
    DateOnly? FromDate,
    DateOnly? ToDate);

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
    string LeaveTypeName);
