using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Auth.Roles.Commands.ArchiveRole;
using ONEVO.Application.Features.Auth.Roles.Commands.AssignRolePermissions;
using ONEVO.Application.Features.Auth.Roles.Commands.CreateRole;
using ONEVO.Application.Features.Auth.Roles.Commands.UpdateRole;
using ONEVO.Application.Features.Auth.Roles.DTOs.Requests;
using ONEVO.Application.Features.Auth.Roles.Queries.GetRoleById;
using ONEVO.Application.Features.Auth.Roles.Queries.ListRoles;

namespace ONEVO.Api.Controllers.Tenant.Roles;

[ApiController]
[Route("api/v1/roles")]
[Authorize(Policy = "TenantPolicy")]
public class RolesController : ControllerBase
{
    private readonly IMediator _mediator;

    public RolesController(IMediator mediator) => _mediator = mediator;

    /// <summary>List all roles for the current tenant.</summary>
    [HttpGet]
    [RequirePermission("roles:read")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListRolesQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Get one role with its assigned permissions.</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("roles:read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetRoleByIdQuery(id), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Create a tenant-scoped role with optional initial permissions.</summary>
    [HttpPost]
    [RequirePermission("roles:manage")]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateRoleCommand(request.Name, request.Description, request.PermissionIds), ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Update a non-system role's name and description.</summary>
    [HttpPatch("{id:guid}")]
    [RequirePermission("roles:manage")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateRoleRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new UpdateRoleCommand(id, request.Name, request.Description), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Soft-archive a non-system role.</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("roles:manage")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ArchiveRoleCommand(id), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Replace the permission set assigned to a non-system role.</summary>
    [HttpPut("{id:guid}/permissions")]
    [RequirePermission("roles:manage")]
    public async Task<IActionResult> AssignPermissions(
        Guid id,
        [FromBody] AssignRolePermissionsRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new AssignRolePermissionsCommand(id, request.PermissionIds), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
