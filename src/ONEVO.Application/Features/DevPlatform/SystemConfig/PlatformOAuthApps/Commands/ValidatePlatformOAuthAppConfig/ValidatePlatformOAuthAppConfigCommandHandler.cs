using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.ValidatePlatformOAuthAppConfig;

/// <summary>
/// LOCAL configuration validation only - this step performs NO live Google/GitHub/
/// Microsoft/Zoom API call and never decrypts secret material. The response makes that
/// explicit via verificationType: "local" so callers cannot mistake it for a live
/// provider check.
/// Checks: provider is approved, app exists, app is active, an active credential exists
/// when the provider requires one, client_id present, default_scopes non-empty.
/// last_verified_at is stamped only when every check passes.
/// </summary>
public sealed record ValidatePlatformOAuthAppConfigCommand(
    string Provider,
    Guid ActorPlatformUserId) : IRequest<Result<OAuthAppValidateConfigResultDto>>;

public sealed class ValidatePlatformOAuthAppConfigCommandHandler
    : IRequestHandler<ValidatePlatformOAuthAppConfigCommand, Result<OAuthAppValidateConfigResultDto>>
{
    private readonly IPlatformOAuthAppRepository _repo;

    public ValidatePlatformOAuthAppConfigCommandHandler(IPlatformOAuthAppRepository repo)
        => _repo = repo;

    public async Task<Result<OAuthAppValidateConfigResultDto>> Handle(
        ValidatePlatformOAuthAppConfigCommand request,
        CancellationToken cancellationToken)
    {
        var provider = PlatformOAuthProviderRules.Normalize(request.Provider);

        if (!PlatformOAuthProviderCatalog.TryGet(provider, out var definition))
            return Result<OAuthAppValidateConfigResultDto>.Failure(
                $"Provider '{provider}' is not an approved OAuth provider.", 400);

        var app = await _repo.GetByProviderAsync(provider, cancellationToken);
        if (app is null)
            return Result<OAuthAppValidateConfigResultDto>.NotFound(
                $"OAuth app for provider '{provider}' was not found.");

        var failures = new List<string>();

        if (!app.IsActive)
            failures.Add("app is not active");

        var activeCredentials = await _repo.GetActiveCredentialsForAppAsync(app.Id, cancellationToken);
        if (definition.ClientSecretRequired && activeCredentials.Count == 0)
            failures.Add("no active credential exists");

        if (string.IsNullOrWhiteSpace(app.ClientId))
            failures.Add("client_id is missing");

        if (!PlatformOAuthProviderRules.IsAbsoluteHttpUrl(app.AuthorizationUrl))
            failures.Add("authorization_url is not an absolute http/https URL");

        if (!PlatformOAuthProviderRules.IsAbsoluteHttpUrl(app.TokenUrl))
            failures.Add("token_url is not an absolute http/https URL");

        if (app.DefaultScopes.Length == 0)
            failures.Add("default_scopes is empty");

        if (failures.Count > 0)
        {
            return Result<OAuthAppValidateConfigResultDto>.Success(new OAuthAppValidateConfigResultDto
            {
                Provider = provider,
                Status = "error",
                VerificationType = "local",
                Message = $"Local validation failed: {string.Join("; ", failures)}.",
                VerifiedAt = null
            });
        }

        var verifiedAt = DateTimeOffset.UtcNow;
        app.LastVerifiedAt = verifiedAt;
        await _repo.SaveChangesAsync(cancellationToken);

        return Result<OAuthAppValidateConfigResultDto>.Success(new OAuthAppValidateConfigResultDto
        {
            Provider = provider,
            Status = "valid",
            VerificationType = "local",
            Message = "Configuration is structurally valid; no provider request was made.",
            VerifiedAt = verifiedAt
        });
    }
}
