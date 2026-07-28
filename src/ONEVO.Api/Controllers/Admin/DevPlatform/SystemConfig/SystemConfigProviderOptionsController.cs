using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.Queries.ListOAuthProviderOptions;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.Queries.ListPaymentGatewayProviderOptions;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.Queries.ListServiceKeyProviderOptions;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;

/// <summary>
/// Read-only provider selection options for System Config screens.
/// These endpoints let the UI present provider choices (cards/dropdowns) instead of
/// requiring the operator to type raw provider keys. No credential material is returned.
/// </summary>
[ApiController]
[Authorize(Policy = "AdminPolicy")]
public sealed class SystemConfigProviderOptionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SystemConfigProviderOptionsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Provider options for the service-key screen (transactional_email, infrastructure,
    /// object_storage, ai_verification families - saved via platform_service_keys).
    /// GET /admin/v1/system-config/service-key-providers
    /// </summary>
    [HttpGet("admin/v1/system-config/service-key-providers")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> ListServiceKeyProviders(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ListServiceKeyProviderOptionsQuery(),
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>
    /// Provider options for the OAuth app screen (oauth_app family - saved via platform_oauth_apps).
    /// GET /admin/v1/system-config/oauth-providers
    /// </summary>
    [HttpGet("admin/v1/system-config/oauth-providers")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> ListOAuthProviders(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ListOAuthProviderOptionsQuery(),
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>
    /// Provider options for the payment gateway screen (payment_gateway family - saved via
    /// payment_gateway_configs).
    /// GET /admin/v1/system-config/payment-gateway-providers
    /// </summary>
    [HttpGet("admin/v1/system-config/payment-gateway-providers")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> ListPaymentGatewayProviders(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ListPaymentGatewayProviderOptionsQuery(),
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
