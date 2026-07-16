using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.DisconnectTenantIntegrationCredential;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Queries.GetTenantIntegrationCredential;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Queries.ListTenantIntegrationCredentials;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;

[ApiController]
[Authorize(Policy = "AdminPolicy")]
[Route("admin/v1/system-config/tenant-integrations")]
public sealed class TenantIntegrationCredentialsController : ControllerBase
{
    private readonly IMediator _mediator;
    public TenantIntegrationCredentialsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> List([FromQuery] Guid tenantId, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListTenantIntegrationCredentialsQuery(tenantId), ct);
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("{id:guid}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetTenantIntegrationCredentialQuery(id), ct);
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/disconnect")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> Disconnect(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DisconnectTenantIntegrationCredentialCommand(id), ct);
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
