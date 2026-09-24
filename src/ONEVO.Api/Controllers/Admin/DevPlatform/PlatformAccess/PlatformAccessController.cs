using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.UpdatePlatformRolePermissions;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.UpdatePlatformUserRoles;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.RevokePlatformUserSession;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.RevokePlatformUserSessions;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.InvitePlatformManager;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.RevokePlatformUserInvite;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.ListPlatformUsers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.GetPlatformUserDetail;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.ListPlatformRoles;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.GetPlatformRoleDetail;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.ListPlatformPermissions;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.ListPlatformUserSessions;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.ListPlatformAuthEvents;

using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.PlatformAccess;

[ApiController]
[Route("admin/v1/platform-access")]
[Authorize(Policy = "AdminPolicy")]
public class PlatformAccessController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlatformAccessController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("users")]
    [RequirePlatformPermission(PlatformPermissionCatalog.AccountsRead)]
    public async Task<IActionResult> ListUsers(CancellationToken ct)
    {
        var response = await _mediator.Send(new ListPlatformUsersQuery(), ct);
        return Ok(response);
    }

    [HttpGet("users/{platformUserId}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.AccountsRead)]
    public async Task<IActionResult> GetUserDetail(Guid platformUserId, CancellationToken ct)
    {
        var response = await _mediator.Send(new GetPlatformUserDetailQuery(platformUserId), ct);
        return Ok(response);
    }

    [HttpGet("roles")]
    [RequirePlatformPermission(PlatformPermissionCatalog.RolesRead)]
    public async Task<IActionResult> ListRoles(CancellationToken ct)
    {
        var response = await _mediator.Send(new ListPlatformRolesQuery(), ct);
        return Ok(response);
    }

    [HttpGet("roles/{roleId}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.RolesRead)]
    public async Task<IActionResult> GetRoleDetail(Guid roleId, CancellationToken ct)
    {
        var response = await _mediator.Send(new GetPlatformRoleDetailQuery(roleId), ct);
        return Ok(response);
    }

    [HttpGet("permissions")]
    [RequirePlatformPermission(PlatformPermissionCatalog.RolesRead)]
    public async Task<IActionResult> ListPermissions(CancellationToken ct)
    {
        var response = await _mediator.Send(new ListPlatformPermissionsQuery(), ct);
        return Ok(response);
    }

    [HttpPut("roles/{roleId}/permissions")]
    [RequirePlatformPermission(PlatformPermissionCatalog.RolesManage)]
    public async Task<IActionResult> UpdateRolePermissions(Guid roleId, [FromBody] UpdatePlatformRolePermissionsRequest request, CancellationToken ct)
    {
        await _mediator.Send(new UpdatePlatformRolePermissionsCommand(roleId, request.Permissions), ct);
        return NoContent();
    }

    [HttpPut("users/{platformUserId}/roles")]
    [RequirePlatformPermission(PlatformPermissionCatalog.AccountsManage)]
    public async Task<IActionResult> UpdateUserRoles(Guid platformUserId, [FromBody] UpdatePlatformUserRolesRequest request, CancellationToken ct)
    {
        await _mediator.Send(new UpdatePlatformUserRolesCommand(platformUserId, request.RoleIds), ct);
        return NoContent();
    }

    [HttpPost("users/invite")]
    [RequirePlatformPermission(PlatformPermissionCatalog.AccountsManage)]
    public async Task<IActionResult> InviteManager([FromBody] InvitePlatformManagerRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new InvitePlatformManagerCommand(request.Email, request.FullName, request.RoleIds), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("users/{platformUserId}/revoke-invite")]
    [RequirePlatformPermission(PlatformPermissionCatalog.AccountsManage)]
    public async Task<IActionResult> RevokeInvite(Guid platformUserId, CancellationToken ct)
    {
        var result = await _mediator.Send(new RevokePlatformUserInviteCommand(platformUserId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("users/{platformUserId}/sessions")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SecurityRead)]
    public async Task<IActionResult> ListUserSessions(Guid platformUserId, CancellationToken ct)
    {
        var response = await _mediator.Send(new ListPlatformUserSessionsQuery(platformUserId), ct);
        return Ok(response);
    }

    [HttpPost("users/{platformUserId}/sessions/{sessionId}/revoke")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SecurityManage)]
    public async Task<IActionResult> RevokeUserSession(Guid platformUserId, Guid sessionId, CancellationToken ct)
    {
        await _mediator.Send(new RevokePlatformUserSessionCommand(platformUserId, sessionId), ct);
        return NoContent();
    }

    [HttpPost("users/{platformUserId}/sessions/revoke-all")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SecurityManage)]
    public async Task<IActionResult> RevokeAllUserSessions(Guid platformUserId, CancellationToken ct)
    {
        await _mediator.Send(new RevokePlatformUserSessionsCommand(platformUserId), ct);
        return NoContent();
    }

    [HttpGet("auth-events")]
    [RequirePlatformPermission(PlatformPermissionCatalog.AuditRead)]
    public async Task<IActionResult> ListAuthEvents(CancellationToken ct)
    {
        var response = await _mediator.Send(new ListPlatformAuthEventsQuery(), ct);
        return Ok(response);
    }
}
