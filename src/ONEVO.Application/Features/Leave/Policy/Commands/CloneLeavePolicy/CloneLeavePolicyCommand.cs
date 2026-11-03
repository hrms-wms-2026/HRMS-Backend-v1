using FluentValidation;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.Commands.Common;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Policy.Entities;

namespace ONEVO.Application.Features.Leave.Policy.Commands.CloneLeavePolicy;

/// <summary>Copies the rules in force today into a new policy for the same leave type. The copy is
/// not assigned to anyone.</summary>
public sealed record CloneLeavePolicyCommand(Guid PolicyId, string Name) : IRequest<Result<Guid>>;

public class CloneLeavePolicyCommandValidator : AbstractValidator<CloneLeavePolicyCommand>
{
    public CloneLeavePolicyCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
    }
}

public sealed class CloneLeavePolicyCommandHandler : IRequestHandler<CloneLeavePolicyCommand, Result<Guid>>
{
    private readonly ILeavePolicyRepository _policies;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CloneLeavePolicyCommandHandler(
        ILeavePolicyRepository policies, ICurrentUser currentUser, IDateTimeProvider clock, IUnitOfWork unitOfWork)
    {
        _policies = policies;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CloneLeavePolicyCommand request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var source = await _policies.GetTrackedAsync(tenantId, request.PolicyId, ct);
        if (source is null)
            return Result<Guid>.NotFound("Leave policy not found.");

        var name = request.Name.Trim();
        if (await _policies.ExistsByNameAsync(tenantId, name, excludingPolicyId: null, ct))
            return Result<Guid>.Conflict("A leave policy with this name already exists.");

        var today = _clock.Today;
        var versions = await _policies.ListVersionsTrackedAsync(tenantId, [source.Id], ct);
        var template = LeavePolicyVersionFactory.InForce(versions, today) ?? versions.MinBy(v => v.EffectiveFrom);
        if (template is null)
            return Result<Guid>.Failure("The policy has no rules to copy.");
        var blackouts = await _policies.ListBlackoutsAsync(tenantId, [template.Id], ct);

        var clone = new LeavePolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeaveTypeId = source.LeaveTypeId,
            Name = name,
            Description = source.Description,
            IsActive = true,
            CreatedAt = _clock.UtcNow,
        };
        var (version, copiedBlackouts) = LeavePolicyVersionFactory.Create(
            tenantId, clone.Id, 1, today, LeavePolicyVersionFactory.ToRules(template, blackouts),
            _currentUser.UserId, _clock.UtcNow);

        await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            _policies.AddPolicy(clone);
            _policies.AddVersion(version, copiedBlackouts);
            await _policies.SaveChangesAsync(innerCt);
            return true;
        }, ct);

        return Result<Guid>.Success(clone.Id);
    }
}
