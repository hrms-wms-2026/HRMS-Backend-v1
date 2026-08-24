using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.RevokeTenantSession;
using ONEVO.Application.Features.DevPlatform.Tenancy.Queries.ListTenantSessions;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Tenants;

[ApiController]
[Route("admin/v1/tenants/{tenantId:guid}/sessions")]
[Authorize(Policy = "AdminPolicy")]
public sealed class AdminTenantSessionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminTenantSessionsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePlatformPermission(PlatformPermissionCatalog.SecurityRead)]
    public async Task<IActionResult> List(Guid tenantId, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListTenantSessionsQuery(tenantId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{sessionId:guid}/revoke")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SecurityManage)]
    public async Task<IActionResult> Revoke(Guid tenantId, Guid sessionId, CancellationToken ct)
    {
        var result = await _mediator.Send(new RevokeTenantSessionCommand(tenantId, sessionId), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
