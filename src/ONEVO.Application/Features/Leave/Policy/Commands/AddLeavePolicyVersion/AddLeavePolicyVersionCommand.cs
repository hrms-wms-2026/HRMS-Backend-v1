using FluentValidation;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.Commands.Common;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Policy.Commands.AddLeavePolicyVersion;

/// <summary>Saves edited rules as a new version that applies from <see cref="ApplyFrom"/> (today or
/// later). Earlier versions are never changed. A future version on the same date is replaced.</summary>
public sealed record AddLeavePolicyVersionCommand(Guid PolicyId, DateOnly ApplyFrom, LeavePolicyRulesInput Rules)
    : IRequest<Result<Guid>>;

public class AddLeavePolicyVersionCommandValidator : AbstractValidator<AddLeavePolicyVersionCommand>
{
    public AddLeavePolicyVersionCommandValidator()
    {
        RuleFor(x => x.Rules).NotNull().SetValidator(new LeavePolicyRulesValidator());
    }
}

public sealed class AddLeavePolicyVersionCommandHandler : IRequestHandler<AddLeavePolicyVersionCommand, Result<Guid>>
{
    private readonly ILeavePolicyRepository _policies;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public AddLeavePolicyVersionCommandHandler(
        ILeavePolicyRepository policies, ICurrentUser currentUser, IDateTimeProvider clock, IUnitOfWork unitOfWork)
    {
        _policies = policies;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(AddLeavePolicyVersionCommand request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var policy = await _policies.GetTrackedAsync(tenantId, request.PolicyId, ct);
        if (policy is null)
            return Result<Guid>.NotFound("Leave policy not found.");
        if (!policy.IsActive)
            return Result<Guid>.Failure("Turn the policy back on before changing its rules.");

        var today = _clock.Today;
        if (request.ApplyFrom < today)
            return Result<Guid>.Failure("Changes can apply from today or a later date, not the past.");

        var versions = await _policies.ListVersionsTrackedAsync(tenantId, [policy.Id], ct);
        var sameDay = versions.FirstOrDefault(v => v.EffectiveFrom == request.ApplyFrom);
        if (sameDay is not null && sameDay.EffectiveFrom <= today)
            return Result<Guid>.Failure("A version of this policy already starts on that date. Choose a later date.");

        var versionNumber = sameDay?.VersionNumber ?? versions.Select(v => v.VersionNumber).DefaultIfEmpty(0).Max() + 1;
        var (version, blackouts) = LeavePolicyVersionFactory.Create(
            tenantId, policy.Id, versionNumber, request.ApplyFrom, request.Rules, _currentUser.UserId, _clock.UtcNow);

        await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            if (sameDay is not null)
            {
                _policies.RemoveVersion(sameDay);
                await _policies.SaveChangesAsync(innerCt);
            }

            _policies.AddVersion(version, blackouts);
            policy.UpdatedAt = _clock.UtcNow;
            await _policies.SaveChangesAsync(innerCt);
            return true;
        }, ct);

        return Result<Guid>.Success(version.Id);
    }
}
