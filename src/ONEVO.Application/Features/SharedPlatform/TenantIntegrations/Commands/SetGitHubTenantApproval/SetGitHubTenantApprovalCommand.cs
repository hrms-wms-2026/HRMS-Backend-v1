using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Mappers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.SetGitHubTenantApproval;

public sealed record SetGitHubTenantApprovalCommand(bool Enabled)
    : IRequest<Result<GitHubTenantApprovalDto>>;

public sealed class SetGitHubTenantApprovalCommandHandler
    : IRequestHandler<SetGitHubTenantApprovalCommand, Result<GitHubTenantApprovalDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ITenantIntegrationCredentialRepository _repository;
    private readonly GitHubUserIntegrationAvailability _availability;

    public SetGitHubTenantApprovalCommandHandler(
        ICurrentUser currentUser,
        ITenantIntegrationCredentialRepository repository,
        GitHubUserIntegrationAvailability availability)
    {
        _currentUser = currentUser;
        _repository = repository;
        _availability = availability;
    }

    public async Task<Result<GitHubTenantApprovalDto>> Handle(
        SetGitHubTenantApprovalCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Enabled)
        {
            return await EnableAsync(cancellationToken);
        }

        return await DisableAsync(cancellationToken);
    }

    private async Task<Result<GitHubTenantApprovalDto>> EnableAsync(CancellationToken ct)
    {
        var available = await _availability.ValidateTenantEnableAsync(_currentUser.TenantId, ct);
        if (!available.IsSuccess)
        {
            return Result<GitHubTenantApprovalDto>.Failure(
                available.Error ?? "GitHub cannot be enabled for this tenant.",
                available.StatusCode ?? 400);
        }

        var approval = await _repository.GetByTenantAndIntegrationAsync(
            _currentUser.TenantId,
            GitHubUserOAuthRules.IntegrationKey,
            ct);
        var now = DateTimeOffset.UtcNow;
        var isNew = approval is null;
        approval ??= new TenantIntegrationCredential
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            IntegrationKey = GitHubUserOAuthRules.IntegrationKey,
            ScopesGranted = [],
            ConnectedAt = now,
            ConnectedByUserId = _currentUser.UserId
        };

        approval.Status = "connected";
        approval.ConnectedAt = now;
        approval.ConnectedByUserId = _currentUser.UserId;
        approval.DisconnectedAt = null;
        approval.ErrorMessage = null;
        ClearTenantTokenState(approval);

        if (isNew)
        {
            await _repository.AddAsync(approval, ct);
        }

        await _repository.SaveChangesAsync(ct);
        return Result<GitHubTenantApprovalDto>.Success(
            GitHubTenantApprovalMapper.ToDto(approval));
    }

    private async Task<Result<GitHubTenantApprovalDto>> DisableAsync(CancellationToken ct)
    {
        var approval = await _repository.GetByTenantAndIntegrationAsync(
            _currentUser.TenantId,
            GitHubUserOAuthRules.IntegrationKey,
            ct);
        if (approval is null)
        {
            return Result<GitHubTenantApprovalDto>.Success(
                GitHubTenantApprovalMapper.ToDto(null));
        }

        approval.Status = "disconnected";
        approval.DisconnectedAt = DateTimeOffset.UtcNow;
        approval.ErrorMessage = null;
        ClearTenantTokenState(approval);

        await _repository.SaveChangesAsync(ct);
        return Result<GitHubTenantApprovalDto>.Success(
            GitHubTenantApprovalMapper.ToDto(approval));
    }

    private static void ClearTenantTokenState(TenantIntegrationCredential approval)
    {
        approval.AccessTokenEncrypted = null;
        approval.RefreshTokenEncrypted = null;
        approval.TokenExpiresAt = null;
        approval.ScopesGranted = [];
        approval.ExternalAccountId = null;
        approval.ExternalAccountName = null;
        approval.LastSyncAt = null;
    }
}
