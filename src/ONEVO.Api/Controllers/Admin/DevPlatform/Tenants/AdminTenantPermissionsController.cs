using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.Tenancy.Queries.GetTenantPermissionCatalog;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Tenants;

[ApiController]
[Route("admin/v1/tenants/{tenantId:guid}/permissions")]
public sealed class AdminTenantPermissionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminTenantPermissionsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Same module-filtered permission catalog as tenant-facing GET /api/v1/permissions.</summary>
    [HttpGet("catalog")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TenantsRead)]
    public async Task<IActionResult> GetCatalog(Guid tenantId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetTenantPermissionCatalogQuery(tenantId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
