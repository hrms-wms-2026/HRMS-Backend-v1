using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Auth.Roles.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.AdminAssignTenantRolePermissions;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.AdminCreateTenantRole;
using ONEVO.Application.Features.DevPlatform.Tenancy.Queries.AdminListTenantRoles;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Tenants;

[ApiController]
[Route("admin/v1/tenants/{tenantId:guid}/roles")]
public sealed class AdminTenantRolesController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminTenantRolesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TenantsRead)]
    public async Task<IActionResult> List(Guid tenantId, CancellationToken ct)
    {
        var result = await _mediator.Send(new AdminListTenantRolesQuery(tenantId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TenantsManage)]
    public async Task<IActionResult> Create(Guid tenantId, [FromBody] CreateRoleRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new AdminCreateTenantRoleCommand(
                tenantId,
                request.Name,
                request.Description,
                request.PermissionIds),
            ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("{roleId:guid}/permissions")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TenantsManage)]
    public async Task<IActionResult> AssignPermissions(
        Guid tenantId,
        Guid roleId,
        [FromBody] AssignRolePermissionsRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new AdminAssignTenantRolePermissionsCommand(tenantId, roleId, request.PermissionIds),
            ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
