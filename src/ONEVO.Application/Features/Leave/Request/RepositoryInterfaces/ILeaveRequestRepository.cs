using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;

public interface ILeaveRequestRepository
{
    Task<bool> HasOverlappingPendingOrApprovedRequestAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);

    Task<IReadOnlyList<LeaveRequestListRow>> ListOwnAsync(
        Guid tenantId,
        Guid employeeId,
        LeaveRequestListFilter filter,
        CancellationToken ct = default);

    Task<IReadOnlyList<LeaveApprovalDelegateRow>> ListActiveDelegatesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> approverEmployeeIds,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);

    Task<int> CountDistinctEmployeesPendingOrApprovedInRangeAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);

    Task AddPendingRequestAsync(LeaveRequestWriteSet writeSet, CancellationToken ct = default);

    Task<bool> AreAvailableFileRecordsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileRecordIds,
        CancellationToken ct = default);
}

public sealed record LeaveRequestWriteSet(
    LeaveRequest Request,
    IReadOnlyList<LeaveRequestApprover> Approvers,
    IReadOnlyList<LeaveRequestDocument> Documents,
    LeaveEntitlement Entitlement);

public sealed record LeaveRequestListFilter(
    string? Status,
    DateOnly? FromDate,
    DateOnly? ToDate,
    Guid? LeaveTypeId);

public sealed record LeaveRequestListRow(
    LeaveRequest Request,
    string LeaveTypeName,
    string LeaveTypeCode);

public sealed record LeaveApprovalDelegateRow(
    Guid ApproverEmployeeId,
    Guid DelegateEmployeeId);
