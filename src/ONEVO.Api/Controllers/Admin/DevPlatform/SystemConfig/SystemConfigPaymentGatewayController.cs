using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.CreatePaymentGateway;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.RotatePaymentGatewayCredential;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.UpdatePaymentGatewayMetadata;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.VerifyGatewayCredentials;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Queries.ListPaymentGateways;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Queries.ResolveGatewayForCountry;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;

/// <summary>
/// System Config - Payment Gateway endpoints.
/// Routes:
///   GET    /admin/v1/system-config/payment-gateways        -> list gateway metadata (no secrets)
///   POST   /admin/v1/system-config/payment-gateways/verify -> verify credentials before save (not persisted)
///   POST   /admin/v1/system-config/payment-gateways        -> create gateway config + credential + country routes
///   POST   /admin/v1/system-config/payment-gateways/{id}/credentials/rotate -> rotate credential
///   PUT    /admin/v1/system-config/payment-gateways/{id} -> update metadata and country routes
///   GET    /admin/v1/payment-gateways/resolve              -> resolve active gateway for country (tenant provisioning)
///
/// SECURITY:
/// - All endpoints require AdminPolicy (platform-admin JWT) + platform.system_config.read/manage permission.
/// - Secrets are NEVER returned by any GET response.
/// - Credential bytes are encrypted by IEncryptionService before any storage operation.
/// </summary>
[ApiController]
[Authorize(Policy = "AdminPolicy")]
public sealed class SystemConfigPaymentGatewayController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentPlatformUserContext _currentUser;

    public SystemConfigPaymentGatewayController(
        IMediator mediator,
        ICurrentPlatformUserContext currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// List all payment gateway configs. Secrets never returned.
    /// GET /admin/v1/system-config/payment-gateways
    /// </summary>
    [HttpGet("admin/v1/system-config/payment-gateways")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> ListGateways(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListPaymentGatewaysQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>
    /// Verify gateway credentials with the live provider - does NOT persist credentials.
    /// POST /admin/v1/system-config/payment-gateways/verify
    /// </summary>
    [HttpPost("admin/v1/system-config/payment-gateways/verify")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> VerifyCredentials(
        [FromBody] VerifyGatewayCredentialsRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new VerifyGatewayCredentialsCommand(request.Provider, request.Credentials), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>
    /// Create payment gateway config with initial credential and country routes.
    /// POST /admin/v1/system-config/payment-gateways
    /// </summary>
    [HttpPost("admin/v1/system-config/payment-gateways")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> CreateGateway(
        [FromBody] CreatePaymentGatewayRequest request,
        CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
            return Forbid();

        var result = await _mediator.Send(new CreatePaymentGatewayCommand(
            request.GatewayKey,
            request.Provider,
            request.Environment,
            request.DisplayName,
            request.LogoUrl,
            request.PublicKey,
            request.MerchantId,
            request.WebhookUrl,
            request.IsActive,
            request.SecretKey,
            request.WebhookSecret,
            request.CountryCodes,
            request.CountryNameSnapshots,
            actorId.Value), ct);

        return result.IsSuccess
            ? Created($"admin/v1/system-config/payment-gateways/{result.Value!.Id}", result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>
    /// Rotate (replace) credentials for an existing gateway config.
    /// POST /admin/v1/system-config/payment-gateways/{id}/credentials/rotate
    /// </summary>
    [HttpPost("admin/v1/system-config/payment-gateways/{id:guid}/credentials/rotate")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> RotateCredential(
        Guid id,
        [FromBody] RotatePaymentGatewayCredentialRequest request,
        CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
            return Forbid();

        var result = await _mediator.Send(new RotatePaymentGatewayCredentialCommand(
            id,
            request.SecretKey,
            request.WebhookSecret,
            actorId.Value), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>
    /// Update payment gateway metadata and country routes. Secrets are not accepted.
    /// PUT /admin/v1/system-config/payment-gateways/{id}
    /// </summary>
    [HttpPut("admin/v1/system-config/payment-gateways/{id:guid}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> UpdateGateway(
        Guid id,
        [FromBody] UpdatePaymentGatewayMetadataRequest request,
        CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
            return Forbid();

        var result = await _mediator.Send(new UpdatePaymentGatewayMetadataCommand(
            id,
            request.DisplayName,
            request.LogoUrl,
            request.PublicKey,
            request.MerchantId,
            request.WebhookUrl,
            request.IsActive,
            request.CountryCodes,
            request.CountryNameSnapshots,
            actorId.Value), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>
    /// Resolve the active gateway for a given country.
    /// Used by tenant provisioning Step 3. Returns IsResolved=false when no route exists.
    /// GET /admin/v1/payment-gateways/resolve?country={code}&amp;environment={env}
    /// </summary>
    [HttpGet("admin/v1/payment-gateways/resolve")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TenantsManage)]
    public async Task<IActionResult> ResolveForCountry(
        [FromQuery] string country,
        [FromQuery] string environment = "production",
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ResolveGatewayForCountryQuery(country, environment), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
