using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.CreatePlatformOAuthApp;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.RotatePlatformOAuthAppSecret;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.SetPlatformOAuthAppActivation;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.UpdatePlatformOAuthApp;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.VerifyPlatformOAuthApp;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Queries.GetPlatformOAuthApp;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Queries.ListPlatformOAuthApps;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;

/// <summary>
/// System Config — Platform OAuth Apps endpoints (ONEVO-owned OAuth app registrations
/// for github/google/microsoft/zoom; Slack is Phase 2).
/// Routes:
///   GET    /admin/v1/system-config/oauth-apps                            → list (no secrets)
///   GET    /admin/v1/system-config/oauth-apps/{provider}                 → detail (no secrets)
///   POST   /admin/v1/system-config/oauth-apps                            → create app + credential v1
///   PUT    /admin/v1/system-config/oauth-apps/{provider}                 → update non-secret metadata
///   POST   /admin/v1/system-config/oauth-apps/{provider}/rotate-secret   → new credential version
///   POST   /admin/v1/system-config/oauth-apps/{provider}/activate        → is_active = true
///   POST   /admin/v1/system-config/oauth-apps/{provider}/deactivate      → is_active = false
///   POST   /admin/v1/system-config/oauth-apps/{provider}/verify          → LOCAL metadata check only
///
/// SECURITY:
/// - All endpoints require AdminPolicy (platform admin) + platform.system_config.read/manage.
/// - Plaintext and encrypted secrets are NEVER returned by any response
///   (only hasActiveCredential/hasPrivateKey booleans + activeCredentialVersion).
/// - clientSecret/privateKey request fields are encrypted by IEncryptionService before
///   persistence and never logged.
/// - No live GitHub/Google/Microsoft/Zoom API is called in this step.
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

    /// <summary>List all OAuth app registrations. Secrets never returned.</summary>
    [HttpGet("admin/v1/system-config/oauth-apps")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> ListOAuthApps(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListPlatformOAuthAppsQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Get one OAuth app by provider slug (case-insensitive). Secrets never returned.</summary>
    [HttpGet("admin/v1/system-config/oauth-apps/{provider}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> GetOAuthApp(string provider, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPlatformOAuthAppQuery(provider), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Create an OAuth app and its first active credential. Secrets encrypted before persistence.</summary>
    [HttpPost("admin/v1/system-config/oauth-apps")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> CreateOAuthApp(
        [FromBody] CreatePlatformOAuthAppRequest request,
        CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
            return Forbid();

        var result = await _mediator.Send(new CreatePlatformOAuthAppCommand(
            request.Provider,
            request.AppName,
            request.LogoUrl,
            request.ClientId,
            request.ClientSecret,
            request.PrivateKey,
            request.AuthorizationUrl,
            request.TokenUrl,
            request.DefaultScopes,
            request.IsActive,
            actorId.Value), ct);

        return result.IsSuccess
            ? Created($"admin/v1/system-config/oauth-apps/{result.Value!.Provider}", result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Update non-secret metadata only. Secret changes must use rotate-secret.</summary>
    [HttpPut("admin/v1/system-config/oauth-apps/{provider}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> UpdateOAuthApp(
        string provider,
        [FromBody] UpdatePlatformOAuthAppRequest request,
        CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
            return Forbid();

        var result = await _mediator.Send(new UpdatePlatformOAuthAppCommand(
            provider,
            request.AppName,
            request.LogoUrl,
            request.ClientId,
            request.AuthorizationUrl,
            request.TokenUrl,
            request.DefaultScopes,
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

    /// <summary>Activate an OAuth app. Requires at least one active credential.</summary>
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
    /// LOCAL metadata verification only (no live provider API call, no decryption).
    /// Stamps last_verified_at when all checks pass.
    /// </summary>
    [HttpPost("admin/v1/system-config/oauth-apps/{provider}/verify")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> VerifyOAuthApp(string provider, CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
            return Forbid();

        var result = await _mediator.Send(new VerifyPlatformOAuthAppCommand(provider, actorId.Value), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
