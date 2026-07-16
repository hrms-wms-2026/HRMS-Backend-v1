using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.CreatePlatformServiceKey;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.RotatePlatformServiceKey;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.SetPlatformServiceKeyActivation;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.UpdatePlatformServiceKey;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.VerifyPlatformServiceKey;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Queries.GetPlatformServiceKey;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Queries.ListPlatformServiceKeys;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;

/// <summary>
/// System Config — Platform Service Keys endpoints (ONEVO-owned Resend/SendGrid/Cloudflare credentials).
/// Routes:
///   GET    /admin/v1/system-config/service-keys                          → list (no secrets)
///   GET    /admin/v1/system-config/service-keys/{serviceKey}             → detail (no secrets)
///   POST   /admin/v1/system-config/service-keys                          → create (encrypts apiKey)
///   PUT    /admin/v1/system-config/service-keys/{serviceKey}             → update metadata (displayName)
///   POST   /admin/v1/system-config/service-keys/{serviceKey}/rotate-key  → replace encrypted key
///   POST   /admin/v1/system-config/service-keys/{serviceKey}/verify     → verify SAVED key (no body)
///   POST   /admin/v1/system-config/service-keys/{serviceKey}/activate   → set is_active = true
///   POST   /admin/v1/system-config/service-keys/{serviceKey}/deactivate → set is_active = false
///
/// SECURITY:
/// - All endpoints require AdminPolicy (platform admin) + platform.system_config.read/manage permission.
/// - Plaintext and encrypted API keys are NEVER returned by any response.
/// - apiKey request fields are encrypted by IEncryptionService before persistence and never logged.
/// </summary>
[ApiController]
[Authorize(Policy = "AdminPolicy")]
public sealed class PlatformServiceKeysController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentPlatformUserContext _currentUser;

    public PlatformServiceKeysController(
        IMediator mediator,
        ICurrentPlatformUserContext currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>List all platform service keys. Secrets never returned.</summary>
    [HttpGet("admin/v1/system-config/service-keys")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> ListServiceKeys(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListPlatformServiceKeysQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Get one platform service key by slug. Secrets never returned.</summary>
    [HttpGet("admin/v1/system-config/service-keys/{serviceKey}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> GetServiceKey(string serviceKey, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPlatformServiceKeyQuery(serviceKey), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Create a platform service key. apiKey is encrypted before persistence.</summary>
    [HttpPost("admin/v1/system-config/service-keys")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> CreateServiceKey(
        [FromBody] CreatePlatformServiceKeyRequest request,
        CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
            return Forbid();

        var result = await _mediator.Send(new CreatePlatformServiceKeyCommand(
            request.ServiceKey,
            request.DisplayName,
            request.ApiKey,
            actorId.Value), ct);

        return result.IsSuccess
            ? Created($"admin/v1/system-config/service-keys/{result.Value!.ServiceKey}", result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Update service key metadata (display name only).</summary>
    [HttpPut("admin/v1/system-config/service-keys/{serviceKey}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> UpdateServiceKey(
        string serviceKey,
        [FromBody] UpdatePlatformServiceKeyRequest request,
        CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
            return Forbid();

        var result = await _mediator.Send(new UpdatePlatformServiceKeyCommand(
            serviceKey, request.DisplayName, actorId.Value), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Rotate (replace) the encrypted API key. Plaintext never stored or returned.</summary>
    [HttpPost("admin/v1/system-config/service-keys/{serviceKey}/rotate-key")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> RotateServiceKey(
        string serviceKey,
        [FromBody] RotatePlatformServiceKeyRequest request,
        CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
            return Forbid();

        var result = await _mediator.Send(new RotatePlatformServiceKeyCommand(
            serviceKey, request.ApiKey, actorId.Value), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>
    /// Verify the SAVED key of this service key (no request body).
    /// Decrypts in memory only; stamps last_verified_at on success.
    /// </summary>
    [HttpPost("admin/v1/system-config/service-keys/{serviceKey}/verify")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> VerifyServiceKey(string serviceKey, CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
            return Forbid();

        var result = await _mediator.Send(new VerifyPlatformServiceKeyCommand(serviceKey, actorId.Value), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Activate a service key (is_active = true).</summary>
    [HttpPost("admin/v1/system-config/service-keys/{serviceKey}/activate")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> ActivateServiceKey(string serviceKey, CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
            return Forbid();

        var result = await _mediator.Send(new SetPlatformServiceKeyActivationCommand(
            serviceKey, true, actorId.Value), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Deactivate a service key (is_active = false). Deactivated keys are never resolved at runtime.</summary>
    [HttpPost("admin/v1/system-config/service-keys/{serviceKey}/deactivate")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> DeactivateServiceKey(string serviceKey, CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
            return Forbid();

        var result = await _mediator.Send(new SetPlatformServiceKeyActivationCommand(
            serviceKey, false, actorId.Value), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
