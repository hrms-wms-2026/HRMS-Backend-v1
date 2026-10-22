using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.Commands.Common;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.Services;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Policy.Commands.EmployeeLeavePolicy;

/// <summary>Gives one employee their own policy for a leave type (beats position, department and
/// company), from today.</summary>
public sealed record SetEmployeeLeavePolicyCommand(Guid EmployeeId, Guid LeaveTypeId, Guid PolicyId) : IRequest<Result>;

/// <summary>Removes the employee's own policy for a leave type so the inherited one applies again.</summary>
public sealed record ResetEmployeeLeavePolicyCommand(Guid EmployeeId, Guid LeaveTypeId) : IRequest<Result>;

public sealed class EmployeeLeavePolicyCommandHandler
    : IRequestHandler<SetEmployeeLeavePolicyCommand, Result>, IRequestHandler<ResetEmployeeLeavePolicyCommand, Result>
{
    private readonly ILeavePolicyRepository _policies;
    private readonly ICurrentUser _currentUser;
    private readonly LeavePolicyAssignmentService _assignments;
    private readonly IUnitOfWork _unitOfWork;

    public EmployeeLeavePolicyCommandHandler(
        ILeavePolicyRepository policies, ICurrentUser currentUser, LeavePolicyAssignmentService assignments, IUnitOfWork unitOfWork)
    {
        _policies = policies;
        _currentUser = currentUser;
        _assignments = assignments;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SetEmployeeLeavePolicyCommand request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        if (await IsOwnRecordWithoutManageAsync(tenantId, request.EmployeeId, ct))
            return Result.Forbidden(OwnRecordMessage);

        var policy = await _policies.GetTrackedAsync(tenantId, request.PolicyId, ct);
        if (policy is null || !policy.IsActive)
            return Result.NotFound("Leave policy not found.");
        if (policy.LeaveTypeId != request.LeaveTypeId)
            return Result.Failure("That policy is for a different leave type.");

        var target = new LeavePolicyAssignmentInput(LeavePolicyTargetKinds.Employee, request.EmployeeId);
        if ((await _policies.ListMissingTargetsAsync(tenantId, [target], ct)).Count > 0)
            return Result.NotFound("Employee not found.");

        var current = await CurrentOverrideAsync(tenantId, request.EmployeeId, request.LeaveTypeId, ct);
        if (current?.LeavePolicyId == policy.Id)
            return Result.Success();

        var actorId = _currentUser.UserId;
        await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            if (current is not null)
            {
                _assignments.End(current, actorId);
                await _policies.SaveChangesAsync(innerCt);
            }

            _policies.AddAssignment(_assignments.NewAssignment(tenantId, policy, target, actorId));
            await _policies.SaveChangesAsync(innerCt);
            return true;
        }, ct);

        return Result.Success();
    }

    public async Task<Result> Handle(ResetEmployeeLeavePolicyCommand request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        if (await IsOwnRecordWithoutManageAsync(tenantId, request.EmployeeId, ct))
            return Result.Forbidden(OwnRecordMessage);

        var current = await CurrentOverrideAsync(tenantId, request.EmployeeId, request.LeaveTypeId, ct);
        if (current is null)
            return Result.Success();

        _assignments.End(current, _currentUser.UserId);
        await _policies.SaveChangesAsync(ct);
        return Result.Success();
    }

    private const string OwnRecordMessage = "You cannot change your own leave policy.";

    /// <summary>leave:create lets a manager set other people's policies; only leave:manage may set
    /// one's own, so nobody can hand themselves a more generous policy.</summary>
    private async Task<bool> IsOwnRecordWithoutManageAsync(Guid tenantId, Guid employeeId, CancellationToken ct) =>
        !_currentUser.HasPermission("leave:manage")
        && await _policies.IsUsersOwnEmployeeAsync(tenantId, employeeId, _currentUser.UserId, ct);

    private async Task<Domain.Features.Leave.Policy.Entities.LeavePolicyAssignment?> CurrentOverrideAsync(
        Guid tenantId, Guid employeeId, Guid leaveTypeId, CancellationToken ct) =>
        (await _policies.ListOpenAssignmentsForLeaveTypeTrackedAsync(tenantId, leaveTypeId, ct))
            .FirstOrDefault(a => a.TargetKind == LeavePolicyTargetKinds.Employee && a.TargetId == employeeId);
}
