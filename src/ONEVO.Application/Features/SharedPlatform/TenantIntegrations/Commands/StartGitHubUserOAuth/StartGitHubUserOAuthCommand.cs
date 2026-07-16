using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.StartGitHubUserOAuth;

public sealed record StartGitHubUserOAuthCommand(string? ReturnUrl, string RedirectUri)
    : IRequest<Result<GitHubOAuthStartResponse>>;

public sealed class StartGitHubUserOAuthCommandHandler
    : IRequestHandler<StartGitHubUserOAuthCommand, Result<GitHubOAuthStartResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly GitHubUserIntegrationAvailability _availability;
    private readonly IOAuthStateProtector _stateProtector;

    public StartGitHubUserOAuthCommandHandler(
        ICurrentUser currentUser,
        GitHubUserIntegrationAvailability availability,
        IOAuthStateProtector stateProtector)
    {
        _currentUser = currentUser;
        _availability = availability;
        _stateProtector = stateProtector;
    }

    public async Task<Result<GitHubOAuthStartResponse>> Handle(
        StartGitHubUserOAuthCommand request,
        CancellationToken cancellationToken)
    {
        var returnUrl = GitHubUserOAuthRules.ValidateReturnUrl(request.ReturnUrl);
        if (!string.IsNullOrWhiteSpace(request.ReturnUrl) && returnUrl is null)
        {
            return Result<GitHubOAuthStartResponse>.Failure(
                "Return URL must be a safe local path.");
        }

        var availability = await _availability.ValidateAsync(
            _currentUser.TenantId,
            cancellationToken);
        if (!availability.IsSuccess)
        {
            return Result<GitHubOAuthStartResponse>.Failure(
                availability.Error ?? "GitHub integration is unavailable.",
                availability.StatusCode ?? 400);
        }

        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.AddMinutes(10);
        var payload = new GitHubOAuthState(
            Guid.NewGuid().ToString("N"),
            _currentUser.TenantId,
            _currentUser.UserId,
            GitHubUserOAuthRules.IntegrationKey,
            GitHubUserOAuthRules.Provider,
            returnUrl,
            issuedAt,
            expiresAt,
            _currentUser.SessionBinding);
        var state = _stateProtector.Protect(payload);
        var app = availability.Value!;
        var authorizationUrl = GitHubUserOAuthRules.BuildAuthorizationUrl(
            app.AuthorizationUrl,
            app.ClientId,
            request.RedirectUri,
            app.DefaultScopes,
            state);

        return Result<GitHubOAuthStartResponse>.Success(
            new GitHubOAuthStartResponse(authorizationUrl, expiresAt));
    }
}
