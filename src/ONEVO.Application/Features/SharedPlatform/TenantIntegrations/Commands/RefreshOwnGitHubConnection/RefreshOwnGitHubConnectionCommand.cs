using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.UpsertOwnUserIntegrationConnection;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.RefreshOwnGitHubConnection;

public sealed record RefreshOwnGitHubConnectionCommand
    : IRequest<Result<UserIntegrationConnectionDto>>;

public sealed class RefreshOwnGitHubConnectionCommandHandler
    : IRequestHandler<RefreshOwnGitHubConnectionCommand, Result<UserIntegrationConnectionDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserIntegrationConnectionRepository _repository;
    private readonly GitHubUserIntegrationAvailability _availability;
    private readonly IPlatformOAuthAppResolver _oauthApps;
    private readonly IGitHubOAuthClient _github;
    private readonly IEncryptionService _encryption;
    private readonly ISender _sender;

    public RefreshOwnGitHubConnectionCommandHandler(
        ICurrentUser currentUser,
        IUserIntegrationConnectionRepository repository,
        GitHubUserIntegrationAvailability availability,
        IPlatformOAuthAppResolver oauthApps,
        IGitHubOAuthClient github,
        IEncryptionService encryption,
        ISender sender)
    {
        _currentUser = currentUser;
        _repository = repository;
        _availability = availability;
        _oauthApps = oauthApps;
        _github = github;
        _encryption = encryption;
        _sender = sender;
    }

    public async Task<Result<UserIntegrationConnectionDto>> Handle(
        RefreshOwnGitHubConnectionCommand request,
        CancellationToken cancellationToken)
    {
        var available = await _availability.ValidateAsync(
            _currentUser.TenantId,
            cancellationToken);
        if (!available.IsSuccess)
        {
            return Result<UserIntegrationConnectionDto>.Failure(
                available.Error ?? "GitHub integration is unavailable.",
                available.StatusCode ?? 400);
        }

        var connection = await _repository.GetActiveAsync(
            _currentUser.TenantId,
            _currentUser.UserId,
            GitHubUserOAuthRules.IntegrationKey,
            cancellationToken);
        if (connection is null)
        {
            return Result<UserIntegrationConnectionDto>.NotFound(
                "GitHub is not connected for the current user.");
        }

        if (string.IsNullOrWhiteSpace(connection.RefreshTokenEncrypted))
        {
            return Result<UserIntegrationConnectionDto>.Failure(
                "This GitHub connection does not support token refresh. Reconnect the account.",
                422);
        }

        var credential = await _oauthApps.GetActiveCredentialForProviderAsync(
            GitHubUserOAuthRules.Provider,
            cancellationToken);
        if (credential is null)
        {
            return Result<UserIntegrationConnectionDto>.Failure(
                "GitHub OAuth credential is unavailable.",
                422);
        }

        var refreshToken = _encryption.Decrypt(connection.RefreshTokenEncrypted);
        var app = available.Value!;
        var refreshed = await _github.RefreshTokenAsync(
            new GitHubOAuthRefreshRequest(
                app.TokenUrl,
                app.ClientId,
                credential.ClientSecret,
                refreshToken),
            cancellationToken);
        if (refreshed is null || string.IsNullOrWhiteSpace(refreshed.AccessToken))
        {
            return Result<UserIntegrationConnectionDto>.Failure(
                "GitHub token refresh failed.",
                502);
        }

        var replacementRefreshToken = string.IsNullOrWhiteSpace(refreshed.RefreshToken)
            ? refreshToken
            : refreshed.RefreshToken;
        var scopes = ParseScopes(refreshed.Scope, connection.ScopesGranted);
        var expiresAt = refreshed.ExpiresInSeconds.HasValue
            ? DateTimeOffset.UtcNow.AddSeconds(refreshed.ExpiresInSeconds.Value)
            : connection.TokenExpiresAt;

        return await _sender.Send(
            new UpsertOwnUserIntegrationConnectionCommand(
                GitHubUserOAuthRules.IntegrationKey,
                connection.ProviderUserId,
                connection.ProviderUsername,
                connection.ProviderEmail,
                refreshed.AccessToken,
                replacementRefreshToken,
                expiresAt,
                scopes),
            cancellationToken);
    }

    private static string[] ParseScopes(string? providerScopes, string[]? existingScopes)
    {
        if (string.IsNullOrWhiteSpace(providerScopes))
        {
            return existingScopes ?? [];
        }

        return providerScopes.Split(
            new[] { ' ', ',' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
