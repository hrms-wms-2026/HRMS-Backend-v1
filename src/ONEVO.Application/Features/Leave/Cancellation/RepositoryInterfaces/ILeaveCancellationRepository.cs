using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.Leave.Cancellation.RepositoryInterfaces;

public interface ILeaveCancellationRepository
{
    Task<LeaveCancellationState?> GetStateAsync(Guid tenantId, Guid requestId, CancellationToken ct = default);

    Task<IReadOnlyList<LeaveRequestDayAllocation>> ListAllocationsAsync(
        Guid tenantId,
        Guid requestId,
        CancellationToken ct = default);

    Task AddAllocationsAsync(
        IReadOnlyList<LeaveRequestDayAllocation> allocations,
        CancellationToken ct = default);

    Task AddBalanceAuditAsync(LeaveBalanceAudit audit, CancellationToken ct = default);

    void SetExpectedVersion(LeaveRequest request, string? expectedVersion);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed record LeaveCancellationState(
    LeaveRequest Request,
    LeaveEntitlement? Entitlement,
    Employee Employee,
    LegalEntity? LegalEntity,
    string LeaveTypeName,
    string LeaveTypeCode,
    IReadOnlyList<LeaveRequestApprover> Approvers,
    IReadOnlyList<LeaveCancellationRecipient> ApproverRecipients);

public sealed record LeaveCancellationRecipient(
    Guid EmployeeId,
    Guid? UserId,
    string? DisplayName);
