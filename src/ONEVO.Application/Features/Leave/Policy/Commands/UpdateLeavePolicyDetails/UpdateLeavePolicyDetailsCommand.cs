using FluentValidation;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Policy.Commands.UpdateLeavePolicyDetails;

/// <summary>Renames a policy or changes its description. Not versioned: the name labels the policy,
/// it is not a rule. The leave type can never change.</summary>
public sealed record UpdateLeavePolicyDetailsCommand(Guid PolicyId, string Name, string? Description) : IRequest<Result>;

public class UpdateLeavePolicyDetailsCommandValidator : AbstractValidator<UpdateLeavePolicyDetailsCommand>
{
    public UpdateLeavePolicyDetailsCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

public sealed class UpdateLeavePolicyDetailsCommandHandler : IRequestHandler<UpdateLeavePolicyDetailsCommand, Result>
{
    private readonly ILeavePolicyRepository _policies;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public UpdateLeavePolicyDetailsCommandHandler(ILeavePolicyRepository policies, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _policies = policies;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(UpdateLeavePolicyDetailsCommand request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var policy = await _policies.GetTrackedAsync(tenantId, request.PolicyId, ct);
        if (policy is null)
            return Result.NotFound("Leave policy not found.");

        var name = request.Name.Trim();
        if (await _policies.ExistsByNameAsync(tenantId, name, excludingPolicyId: policy.Id, ct))
            return Result.Conflict("A leave policy with this name already exists.");

        policy.Name = name;
        policy.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        policy.UpdatedAt = _clock.UtcNow;
        await _policies.SaveChangesAsync(ct);
        return Result.Success();
    }
}
