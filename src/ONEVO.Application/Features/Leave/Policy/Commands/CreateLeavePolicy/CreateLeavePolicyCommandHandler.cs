using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.Commands.Common;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.Services;
using ONEVO.Domain.Features.Leave.Policy.Entities;

namespace ONEVO.Application.Features.Leave.Policy.Commands.CreateLeavePolicy;

public sealed class CreateLeavePolicyCommandHandler : IRequestHandler<CreateLeavePolicyCommand, Result<LeavePolicyWriteResult>>
{
    private readonly ILeavePolicyRepository _policies;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly LeavePolicyAssignmentService _assignments;

    public CreateLeavePolicyCommandHandler(
        ILeavePolicyRepository policies, ICurrentUser currentUser, IDateTimeProvider clock, LeavePolicyAssignmentService assignments)
    {
        _policies = policies;
        _currentUser = currentUser;
        _clock = clock;
        _assignments = assignments;
    }

    public async Task<Result<LeavePolicyWriteResult>> Handle(CreateLeavePolicyCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeavePolicyWriteResult>.Forbidden("Authentication required.");
        var tenantId = _currentUser.TenantId;

        var leaveType = await _policies.GetLeaveTypeAsync(tenantId, request.LeaveTypeId, ct);
        if (leaveType is null || !leaveType.IsActive)
            return Result<LeavePolicyWriteResult>.NotFound("The selected leave type no longer exists.");

        var name = request.Name.Trim();
        if (await _policies.ExistsByNameAsync(tenantId, name, excludingPolicyId: null, ct))
            return Result<LeavePolicyWriteResult>.Conflict("A leave policy with this name already exists.");

        var missing = await _policies.ListMissingTargetsAsync(tenantId, request.Assignments, ct);
        if (missing.Count > 0)
            return Result<LeavePolicyWriteResult>.Failure("Some of the selected company, departments or positions no longer exist.");

        var policy = new LeavePolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeaveTypeId = leaveType.Id,
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            IsActive = true,
            CreatedAt = _clock.UtcNow,
        };

        var plan = await _assignments.PlanPolicyTargetsAsync(tenantId, policy.Id, policy.LeaveTypeId, request.Assignments, ct);
        if (plan.Conflicts.Count > 0 && !request.ReplaceConflicts)
            return Result<LeavePolicyWriteResult>.Success(LeavePolicyWriteResult.NeedsConfirmation(plan.Conflicts));

        var actorId = _currentUser.UserId;
        var (version, blackouts) = LeavePolicyVersionFactory.Create(
            tenantId, policy.Id, 1, _clock.Today, request.Rules, actorId, _clock.UtcNow);

        await _assignments.ApplyAsync(tenantId, policy, plan, actorId, ct, beforeSave: () =>
        {
            _policies.AddPolicy(policy);
            _policies.AddVersion(version, blackouts);
        });

        return Result<LeavePolicyWriteResult>.Success(LeavePolicyWriteResult.Written(policy.Id));
    }
}
