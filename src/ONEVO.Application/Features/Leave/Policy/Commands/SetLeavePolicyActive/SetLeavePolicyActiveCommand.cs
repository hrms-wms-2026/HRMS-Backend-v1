using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Policy.Commands.SetLeavePolicyActive;

/// <summary>Turns a policy off or back on. A policy that is still assigned anywhere cannot be turned
/// off, so turning one off never silently changes who gets which leave.</summary>
public sealed record SetLeavePolicyActiveCommand(Guid PolicyId, bool IsActive) : IRequest<Result>;

public sealed class SetLeavePolicyActiveCommandHandler : IRequestHandler<SetLeavePolicyActiveCommand, Result>
{
    public const string HasAssignmentsCode = "leave_policy.has_assignments";

    private readonly ILeavePolicyRepository _policies;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public SetLeavePolicyActiveCommandHandler(ILeavePolicyRepository policies, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _policies = policies;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(SetLeavePolicyActiveCommand request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var policy = await _policies.GetTrackedAsync(tenantId, request.PolicyId, ct);
        if (policy is null)
            return Result.NotFound("Leave policy not found.");
        if (policy.IsActive == request.IsActive)
            return Result.Success();

        if (!request.IsActive)
        {
            var open = await _policies.ListOpenAssignmentsForPoliciesAsync(tenantId, [policy.Id], ct);
            if (open.Count > 0)
                return Result.Failure("Unassign this policy everywhere before turning it off.", 409, HasAssignmentsCode);
        }

        policy.IsActive = request.IsActive;
        policy.UpdatedAt = _clock.UtcNow;
        await _policies.SaveChangesAsync(ct);
        return Result.Success();
    }
}
