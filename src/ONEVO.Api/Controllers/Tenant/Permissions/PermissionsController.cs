using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Auth.Permission.Queries.ListTenantPermissionCatalog;

namespace ONEVO.Api.Controllers.Tenant.Permissions;

[ApiController]
[Route("api/v1/permissions")]
[Authorize(Policy = "TenantPolicy")]
public sealed class PermissionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PermissionsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Permission catalog for the current tenant (assignable + universal read-only).</summary>
    [HttpGet]
    [RequirePermission("roles:read")]
    public async Task<IActionResult> ListCatalog(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListTenantPermissionCatalogQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
