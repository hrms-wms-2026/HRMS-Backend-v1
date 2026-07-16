using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.VerifyPlatformOAuthApp;

/// <summary>
/// LOCAL metadata verification only — this step performs NO live GitHub/Google/
/// Microsoft/Zoom API calls and never decrypts secret material.
/// Checks: app exists, app is active, an active credential exists, both URLs are
/// absolute http/https, client_id present, default_scopes non-empty.
/// last_verified_at is stamped only when every check passes.
/// </summary>
public sealed record VerifyPlatformOAuthAppCommand(
    string Provider,
    Guid ActorPlatformUserId) : IRequest<Result<OAuthAppVerificationResultDto>>;

public sealed class VerifyPlatformOAuthAppCommandHandler
    : IRequestHandler<VerifyPlatformOAuthAppCommand, Result<OAuthAppVerificationResultDto>>
{
    private readonly IPlatformOAuthAppRepository _repo;

    public VerifyPlatformOAuthAppCommandHandler(IPlatformOAuthAppRepository repo)
        => _repo = repo;

    public async Task<Result<OAuthAppVerificationResultDto>> Handle(
        VerifyPlatformOAuthAppCommand request,
        CancellationToken cancellationToken)
    {
        var provider = PlatformOAuthProviderRules.Normalize(request.Provider);

        var app = await _repo.GetByProviderAsync(provider, cancellationToken);
        if (app is null)
            return Result<OAuthAppVerificationResultDto>.NotFound(
                $"OAuth app for provider '{provider}' was not found.");

        var failures = new List<string>();

        if (!app.IsActive)
            failures.Add("app is not active");

        var activeCredentials = await _repo.GetActiveCredentialsForAppAsync(app.Id, cancellationToken);
        if (activeCredentials.Count == 0)
            failures.Add("no active credential exists");

        if (!PlatformOAuthProviderRules.IsAbsoluteHttpUrl(app.AuthorizationUrl))
            failures.Add("authorization_url is not an absolute http/https URL");

        if (!PlatformOAuthProviderRules.IsAbsoluteHttpUrl(app.TokenUrl))
            failures.Add("token_url is not an absolute http/https URL");

        if (string.IsNullOrWhiteSpace(app.ClientId))
            failures.Add("client_id is missing");

        if (app.DefaultScopes.Length == 0)
            failures.Add("default_scopes is empty");

        if (failures.Count > 0)
        {
            return Result<OAuthAppVerificationResultDto>.Success(new OAuthAppVerificationResultDto
            {
                Provider = provider,
                Status = "error",
                Message = $"Local verification failed: {string.Join("; ", failures)}.",
                VerifiedAt = null
            });
        }

        var verifiedAt = DateTimeOffset.UtcNow;
        app.LastVerifiedAt = verifiedAt;
        await _repo.SaveChangesAsync(cancellationToken);

        return Result<OAuthAppVerificationResultDto>.Success(new OAuthAppVerificationResultDto
        {
            Provider = provider,
            Status = "healthy",
            Message = "Local OAuth app configuration is valid. No live provider call was made.",
            VerifiedAt = verifiedAt
        });
    }
}
