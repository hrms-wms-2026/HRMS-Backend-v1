using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.ConfigurePlatformOAuthApp;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.RotatePlatformOAuthAppSecret;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.SetPlatformOAuthAppActivation;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.ValidatePlatformOAuthAppConfig;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Queries.GetPlatformOAuthApp;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Queries.ListPlatformOAuthApps;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;

/// <summary>
/// System Config - Platform OAuth Apps endpoints. Approved providers only: github,
/// google, microsoft, zoom (Slack is Phase 2). Provider protocol metadata (authorization
/// URL, token URL, default scopes) is backend-owned via PlatformOAuthProviderCatalog -
/// no endpoint accepts it from the request body.
/// Routes:
///   GET    /admin/v1/system-config/oauth-apps                              -> list all approved provider cards
///   GET    /admin/v1/system-config/oauth-apps/{provider}                   -> detail (no secrets)
///   PUT    /admin/v1/system-config/oauth-apps/{provider}                   -> configure (upsert) approved provider
///   POST   /admin/v1/system-config/oauth-apps/{provider}/rotate-secret     -> new credential version
///   POST   /admin/v1/system-config/oauth-apps/{provider}/activate          -> is_active = true
///   POST   /admin/v1/system-config/oauth-apps/{provider}/deactivate        -> is_active = false
///   POST   /admin/v1/system-config/oauth-apps/{provider}/validate-config   -> LOCAL metadata check only
///
/// There is deliberately no POST (arbitrary-provider create) endpoint: only the four
/// catalog-approved providers can ever exist as configurable rows.
///
/// SECURITY:
/// - All endpoints require AdminPolicy (platform admin) + platform.system_config.read/manage.
/// - Plaintext and encrypted secrets are NEVER returned by any response
///   (only hasActiveCredential/hasPrivateKey booleans + activeCredentialVersion).
/// - clientSecret/privateKey request fields are encrypted by IEncryptionService before
///   persistence and never logged.
/// - No live GitHub/Google/Microsoft/Zoom API is called by this controller.
/// </summary>
[ApiController]
[Authorize(Policy = "AdminPolicy")]
public sealed class PlatformOAuthAppsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentPlatformUserContext _currentUser;

    public PlatformOAuthAppsController(
        IMediator mediator,
        ICurrentPlatformUserContext currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>List every approved provider's card. Secrets never returned.</summary>
    [HttpGet("admin/v1/system-config/oauth-apps")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> ListOAuthApps(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListPlatformOAuthAppsQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Get one approved provider's card by slug (case-insensitive). Secrets never returned.</summary>
    [HttpGet("admin/v1/system-config/oauth-apps/{provider}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> GetOAuthApp(string provider, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPlatformOAuthAppQuery(provider), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>
    /// Configure (upsert) an approved OAuth provider. The provider comes from the route
    /// only; the request body cannot contain provider/authorizationUrl/tokenUrl/
    /// defaultScopes - those are always backend-owned. Secrets are encrypted before
    /// persistence.
    /// </summary>
    [HttpPut("admin/v1/system-config/oauth-apps/{provider}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> ConfigureOAuthApp(
        string provider,
        [FromBody] ConfigurePlatformOAuthAppRequest request,
        CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
            return Forbid();

        var result = await _mediator.Send(new ConfigurePlatformOAuthAppCommand(
            provider,
            request.AppName,
            request.LogoUrl,
            request.ClientId,
            request.ClientSecret,
            request.PrivateKey,
            request.IsActive,
            actorId.Value), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Rotate secret material: deactivates the old credential row and adds a new active version.</summary>
    [HttpPost("admin/v1/system-config/oauth-apps/{provider}/rotate-secret")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> RotateOAuthAppSecret(
        string provider,
        [FromBody] RotatePlatformOAuthAppSecretRequest request,
        CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
            return Forbid();

        var result = await _mediator.Send(new RotatePlatformOAuthAppSecretCommand(
            provider, request.ClientSecret, request.PrivateKey, actorId.Value), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Activate an OAuth app. Requires clientId and, if required, an active credential.</summary>
    [HttpPost("admin/v1/system-config/oauth-apps/{provider}/activate")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> ActivateOAuthApp(string provider, CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
            return Forbid();

        var result = await _mediator.Send(new SetPlatformOAuthAppActivationCommand(
            provider, true, actorId.Value), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Deactivate an OAuth app (is_active = false). Credential rows are kept.</summary>
    [HttpPost("admin/v1/system-config/oauth-apps/{provider}/deactivate")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> DeactivateOAuthApp(string provider, CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
            return Forbid();

        var result = await _mediator.Send(new SetPlatformOAuthAppActivationCommand(
            provider, false, actorId.Value), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>
    /// LOCAL configuration validation only (no live provider API call, no decryption).
    /// Stamps last_verified_at when all checks pass. Response makes explicit that this
    /// is a local structural check, not a live provider verification.
    /// </summary>
    [HttpPost("admin/v1/system-config/oauth-apps/{provider}/validate-config")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> ValidateOAuthAppConfig(string provider, CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
            return Forbid();

        var result = await _mediator.Send(new ValidatePlatformOAuthAppConfigCommand(provider, actorId.Value), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
