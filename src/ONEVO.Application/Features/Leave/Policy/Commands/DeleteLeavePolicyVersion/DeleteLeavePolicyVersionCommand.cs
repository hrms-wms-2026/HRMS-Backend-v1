using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Policy.Commands.DeleteLeavePolicyVersion;

/// <summary>Cancels a scheduled (future) version. Versions already in force are history and stay.</summary>
public sealed record DeleteLeavePolicyVersionCommand(Guid PolicyId, Guid VersionId) : IRequest<Result>;

public sealed class DeleteLeavePolicyVersionCommandHandler : IRequestHandler<DeleteLeavePolicyVersionCommand, Result>
{
    private readonly ILeavePolicyRepository _policies;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public DeleteLeavePolicyVersionCommandHandler(ILeavePolicyRepository policies, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _policies = policies;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(DeleteLeavePolicyVersionCommand request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var versions = await _policies.ListVersionsTrackedAsync(tenantId, [request.PolicyId], ct);
        var version = versions.FirstOrDefault(v => v.Id == request.VersionId);
        if (version is null)
            return Result.NotFound("Policy version not found.");
        if (version.EffectiveFrom <= _clock.Today)
            return Result.Failure("This version is already in force and cannot be removed.");
        if (versions.Count == 1)
            return Result.Failure("A policy needs at least one version.");

        _policies.RemoveVersion(version);
        await _policies.SaveChangesAsync(ct);
        return Result.Success();
    }
}
