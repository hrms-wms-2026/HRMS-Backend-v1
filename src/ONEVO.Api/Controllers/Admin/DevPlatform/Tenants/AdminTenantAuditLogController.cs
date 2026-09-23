using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.Tenancy.Queries.ListTenantAuditLog;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Tenants;

[ApiController]
[Route("admin/v1/tenants/{tenantId:guid}/audit-log")]
[Authorize(Policy = "AdminPolicy")]
[RequirePlatformPermission(PlatformPermissionCatalog.AuditRead)]
public sealed class AdminTenantAuditLogController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminTenantAuditLogController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(
        Guid tenantId,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 25,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListTenantAuditLogQuery(tenantId, page, pageSize), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
