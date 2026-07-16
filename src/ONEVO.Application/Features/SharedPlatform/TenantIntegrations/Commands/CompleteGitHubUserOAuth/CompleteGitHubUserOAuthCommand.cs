using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.UpsertOwnUserIntegrationConnection;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.CompleteGitHubUserOAuth;

public sealed record CompleteGitHubUserOAuthCommand(
    string? Code,
    string? State,
    string RedirectUri)
    : IRequest<Result<GitHubOAuthCompleteResponse>>;

public sealed class CompleteGitHubUserOAuthCommandHandler
    : IRequestHandler<CompleteGitHubUserOAuthCommand, Result<GitHubOAuthCompleteResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly GitHubUserIntegrationAvailability _availability;
    private readonly IOAuthStateProtector _stateProtector;
    private readonly IPlatformOAuthAppResolver _oauthApps;
    private readonly IGitHubOAuthClient _github;
    private readonly ISender _sender;

    public CompleteGitHubUserOAuthCommandHandler(
        ICurrentUser currentUser,
        GitHubUserIntegrationAvailability availability,
        IOAuthStateProtector stateProtector,
        IPlatformOAuthAppResolver oauthApps,
        IGitHubOAuthClient github,
        ISender sender)
    {
        _currentUser = currentUser;
        _availability = availability;
        _stateProtector = stateProtector;
        _oauthApps = oauthApps;
        _github = github;
        _sender = sender;
    }

    public async Task<Result<GitHubOAuthCompleteResponse>> Handle(
        CompleteGitHubUserOAuthCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Result<GitHubOAuthCompleteResponse>.Failure("OAuth code is required.");
        }

        if (string.IsNullOrWhiteSpace(request.State))
        {
            return Result<GitHubOAuthCompleteResponse>.Failure("OAuth state is required.");
        }

        if (!_stateProtector.TryUnprotect(request.State, out var state) || state is null)
        {
            return Result<GitHubOAuthCompleteResponse>.Failure("OAuth state is invalid.");
        }

        var stateError = ValidateState(state);
        if (stateError is not null)
        {
            return Result<GitHubOAuthCompleteResponse>.Failure(stateError);
        }

        var availability = await _availability.ValidateAsync(
            _currentUser.TenantId,
            cancellationToken);
        if (!availability.IsSuccess)
        {
            return Result<GitHubOAuthCompleteResponse>.Failure(
                availability.Error ?? "GitHub integration is unavailable.",
                availability.StatusCode ?? 400);
        }

        var credential = await _oauthApps.GetActiveCredentialForProviderAsync(
            GitHubUserOAuthRules.Provider,
            cancellationToken);
        if (credential is null)
        {
            return Result<GitHubOAuthCompleteResponse>.Failure(
                "GitHub OAuth credential is unavailable.",
                422);
        }

        var app = availability.Value!;
        var token = await _github.ExchangeCodeAsync(
            new GitHubOAuthTokenRequest(
                app.TokenUrl,
                app.ClientId,
                credential.ClientSecret,
                request.Code,
                request.RedirectUri),
            cancellationToken);
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
        {
            return Result<GitHubOAuthCompleteResponse>.Failure(
                "GitHub authorization failed.",
                502);
        }

        var profile = await _github.GetCurrentUserAsync(token.AccessToken, cancellationToken);
        var scopes = ParseScopes(token.Scope, app.DefaultScopes);
        var expiresAt = CalculateExpiry(token.ExpiresInSeconds);
        var stored = await _sender.Send(
            new UpsertOwnUserIntegrationConnectionCommand(
                GitHubUserOAuthRules.IntegrationKey,
                profile?.ProviderUserId,
                profile?.Username,
                profile?.Email,
                token.AccessToken,
                token.RefreshToken,
                expiresAt,
                scopes),
            cancellationToken);
        if (!stored.IsSuccess)
        {
            return Result<GitHubOAuthCompleteResponse>.Failure(
                stored.Error ?? "GitHub connection could not be stored.",
                stored.StatusCode ?? 400);
        }

        return Result<GitHubOAuthCompleteResponse>.Success(
            new GitHubOAuthCompleteResponse(
                GitHubUserOAuthRules.IntegrationKey,
                "connected",
                profile?.Username,
                state.ReturnUrl));
    }

    private string? ValidateState(GitHubOAuthState state)
    {
        if (state.ExpiresAtUtc <= DateTimeOffset.UtcNow ||
            state.IssuedAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return "OAuth state has expired.";
        }

        if (state.TenantId != _currentUser.TenantId)
        {
            return "OAuth state does not match the current tenant.";
        }

        if (state.UserId != _currentUser.UserId)
        {
            return "OAuth state does not match the current user.";
        }

        if (!string.Equals(
                state.IntegrationKey,
                GitHubUserOAuthRules.IntegrationKey,
                StringComparison.Ordinal) ||
            !string.Equals(
                state.Provider,
                GitHubUserOAuthRules.Provider,
                StringComparison.Ordinal))
        {
            return "OAuth state is invalid for GitHub.";
        }

        if (GitHubUserOAuthRules.ValidateReturnUrl(state.ReturnUrl) != state.ReturnUrl)
        {
            return "OAuth return URL is invalid.";
        }

        if (!string.Equals(
                state.SessionBinding,
                _currentUser.SessionBinding,
                StringComparison.Ordinal))
        {
            return "OAuth state does not match the current session.";
        }

        return null;
    }

    private static DateTimeOffset? CalculateExpiry(long? expiresInSeconds)
    {
        if (!expiresInSeconds.HasValue)
        {
            return null;
        }

        return DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds.Value);
    }

    private static string[] ParseScopes(
        string? providerScopes,
        IReadOnlyList<string> defaults)
    {
        if (string.IsNullOrWhiteSpace(providerScopes))
        {
            return defaults.ToArray();
        }

        return providerScopes.Split(
            new[] { ' ', ',' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
