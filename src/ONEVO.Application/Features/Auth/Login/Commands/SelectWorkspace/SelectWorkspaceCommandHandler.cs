using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.Auth.Login.Commands.SelectWorkspace;

public class SelectWorkspaceCommandHandler : IRequestHandler<SelectWorkspaceCommand, Result<LoginResponseDto>>
{
    private const int MaximumWorkspaceSelectionAttempts = 5;
    private const string GenericSelectionFailureMessage = "Sign-in selection expired. Please sign in again.";

    private readonly ILoginWorkspaceSelectionChallengeRepository _workspaceChallenges;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;
    private readonly ILoginContinuationService _continuation;

    public SelectWorkspaceCommandHandler(
        ILoginWorkspaceSelectionChallengeRepository workspaceChallenges,
        ITenantContextSwitcher tenantSwitcher,
        ITenantRepository tenants,
        IUserRepository users,
        ILoginContinuationService continuation)
    {
        _workspaceChallenges = workspaceChallenges;
        _tenantSwitcher = tenantSwitcher;
        _tenants = tenants;
        _users = users;
        _continuation = continuation;
    }

    public async Task<Result<LoginResponseDto>> Handle(SelectWorkspaceCommand request, CancellationToken cancellationToken)
    {
        var challengeState = await _workspaceChallenges.GetActiveAsync(request.LoginChallenge, cancellationToken);
        if (challengeState is null)
            return GenericSelectionFailure();

        var selectedCandidate = challengeState.Candidates
            .FirstOrDefault(c => string.Equals(c.Slug, request.Workspace, StringComparison.Ordinal));

        if (selectedCandidate is null)
        {
            await _workspaceChallenges.RegisterFailedAttemptAsync(
                request.LoginChallenge, MaximumWorkspaceSelectionAttempts, cancellationToken);
            return GenericSelectionFailure();
        }

        var tenant = await _tenants.GetByIdAsync(selectedCandidate.TenantId, cancellationToken);
        if (tenant is null || tenant.Status is not (TenantStatus.Active or TenantStatus.Trial))
        {
            await _workspaceChallenges.TryConsumeAsync(request.LoginChallenge, cancellationToken);
            return GenericSelectionFailure();
        }

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, null), cancellationToken);

        var user = await _users.GetByIdAsync(selectedCandidate.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            await _workspaceChallenges.TryConsumeAsync(request.LoginChallenge, cancellationToken);
            return GenericSelectionFailure();
        }

        var consumedState = await _workspaceChallenges.TryConsumeAsync(request.LoginChallenge, cancellationToken);
        if (consumedState is null)
            return GenericSelectionFailure();

        var continuationRequest = new LoginContinuationRequest(
            tenant.Id,
            user.Id,
            SwitchTenantContext: false,
            GenericFailureMessage: GenericSelectionFailureMessage,
            LegalChallengeOrigin: consumedState.Origin,
            IpAddress: request.IpAddress,
            UserAgent: request.UserAgent,
            FinalizationMode: LoginFinalizationMode.BaseDomainExchange);

        return await _continuation.ContinueAsync(continuationRequest, cancellationToken);
    }

    private static Result<LoginResponseDto> GenericSelectionFailure() =>
        Result<LoginResponseDto>.Failure(GenericSelectionFailureMessage, 401);
}
