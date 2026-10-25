using FluentValidation;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.Commands.Common;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.Services;

namespace ONEVO.Application.Features.Leave.Policy.Commands.SetLeavePolicyAssignments;

/// <summary>Replaces the company/department/position targets of a policy from today. Employee-level
/// overrides are untouched.</summary>
public sealed record SetLeavePolicyAssignmentsCommand(
    Guid PolicyId, IReadOnlyList<LeavePolicyAssignmentInput> Assignments, bool ReplaceConflicts)
    : IRequest<Result<LeavePolicyWriteResult>>;

public class SetLeavePolicyAssignmentsCommandValidator : AbstractValidator<SetLeavePolicyAssignmentsCommand>
{
    public SetLeavePolicyAssignmentsCommandValidator()
    {
        RuleFor(x => x.Assignments).SetValidator(new LeavePolicyTargetsValidator());
    }
}

public sealed class SetLeavePolicyAssignmentsCommandHandler
    : IRequestHandler<SetLeavePolicyAssignmentsCommand, Result<LeavePolicyWriteResult>>
{
    private readonly ILeavePolicyRepository _policies;
    private readonly ICurrentUser _currentUser;
    private readonly LeavePolicyAssignmentService _assignments;

    public SetLeavePolicyAssignmentsCommandHandler(
        ILeavePolicyRepository policies, ICurrentUser currentUser, LeavePolicyAssignmentService assignments)
    {
        _policies = policies;
        _currentUser = currentUser;
        _assignments = assignments;
    }

    public async Task<Result<LeavePolicyWriteResult>> Handle(SetLeavePolicyAssignmentsCommand request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var policy = await _policies.GetTrackedAsync(tenantId, request.PolicyId, ct);
        if (policy is null)
            return Result<LeavePolicyWriteResult>.NotFound("Leave policy not found.");
        if (!policy.IsActive)
            return Result<LeavePolicyWriteResult>.Failure("Turn the policy back on before assigning it.");

        var missing = await _policies.ListMissingTargetsAsync(tenantId, request.Assignments, ct);
        if (missing.Count > 0)
            return Result<LeavePolicyWriteResult>.Failure("Some of the selected company, departments or positions no longer exist.");

        var plan = await _assignments.PlanPolicyTargetsAsync(tenantId, policy.Id, policy.LeaveTypeId, request.Assignments, ct);
        if (plan.Conflicts.Count > 0 && !request.ReplaceConflicts)
            return Result<LeavePolicyWriteResult>.Success(LeavePolicyWriteResult.NeedsConfirmation(plan.Conflicts));

        if (plan.ToEnd.Count > 0 || plan.ToAdd.Count > 0)
            await _assignments.ApplyAsync(tenantId, policy, plan, _currentUser.UserId, ct);

        return Result<LeavePolicyWriteResult>.Success(LeavePolicyWriteResult.Written(policy.Id));
    }
}
