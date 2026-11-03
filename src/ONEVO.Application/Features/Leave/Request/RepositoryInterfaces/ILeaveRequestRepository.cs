using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;

public interface ILeaveRequestRepository
{
    Task<bool> HasOverlappingPendingOrApprovedRequestAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        CancellationToken ct = default);

    /// <summary>The same overlap check, ignoring one request (that request's own dates being edited).</summary>
    Task<bool> HasOverlappingPendingOrApprovedRequestExceptAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        Guid excludeRequestId,
        CancellationToken ct = default);

    Task<IReadOnlyList<LeaveRequestListRow>> ListOwnAsync(
        Guid tenantId,
        Guid employeeId,
        LeaveRequestListFilter filter,
        CancellationToken ct = default);

    /// <summary>Approver employee ids per request (every step, any status) - who may open each in the inbox history.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ListApproverEmployeeIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> requestIds, CancellationToken ct = default);

    Task<IReadOnlyList<LeaveApprovalDelegateRow>> ListActiveDelegatesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> approverEmployeeIds,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);

    Task<IReadOnlyList<LeaveApprovalDelegateListRow>> ListDelegatesForApproverAsync(
        Guid tenantId,
        Guid approverEmployeeId,
        CancellationToken ct = default);

    Task AddDelegateAsync(LeaveApprovalDelegate entity, CancellationToken ct = default);

    Task<LeaveApprovalDelegate?> GetTrackedDelegateAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);

    void RemoveDelegate(LeaveApprovalDelegate entity);

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
    IReadOnlyList<LeaveRequestDayAllocation> DayAllocations,
    LeaveEntitlement Entitlement);

public sealed record LeaveRequestListFilter(
    string? Status,
    DateOnly? FromDate,
    DateOnly? ToDate,
    Guid? LeaveTypeId,
    // Inbox case history: CreatedAt in [CreatedFrom, CreatedTo).
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null);

public sealed record LeaveRequestListRow(
    LeaveRequest Request,
    string LeaveTypeName,
    string LeaveTypeCode,
    string? ApprovedByName = null);

public sealed record LeaveApprovalDelegateRow(
    Guid ApproverEmployeeId,
    Guid DelegateEmployeeId);

public sealed record LeaveApprovalDelegateListRow(
    Guid Id,
    Guid DelegateEmployeeId,
    string DelegateName,
    DateOnly StartDate,
    DateOnly EndDate);
