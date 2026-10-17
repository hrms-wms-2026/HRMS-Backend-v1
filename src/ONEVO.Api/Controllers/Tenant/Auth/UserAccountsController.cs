using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Auth.UserAccounts.Commands.ReactivateUserAccount;
using ONEVO.Application.Features.Auth.UserAccounts.Commands.RevokeUserAccountSession;
using ONEVO.Application.Features.Auth.UserAccounts.Commands.SignOutUserAccount;
using ONEVO.Application.Features.Auth.UserAccounts.Commands.SuspendUserAccount;
using ONEVO.Application.Features.Auth.UserAccounts.Queries.GetUserAccountSessions;
using ONEVO.Application.Features.Auth.UserAccounts.Queries.ListUserAccounts;

namespace ONEVO.Api.Controllers.Tenant.Auth;

/// <summary>
/// Settings &gt; Users: tenant user accounts. users:read lists them; users:manage suspends,
/// reactivates and signs them out. Invitation resend/revoke stay on the employee endpoints
/// (POST /employees/{id}/resend-invitation, /revoke-invitation), which accept users:manage too.
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = "TenantPolicy")]
public class UserAccountsController : ControllerBase
{
    private readonly IMediator _mediator;

    public UserAccountsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("users:read")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListUserAccountsQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/suspend")]
    [RequirePermission("users:manage")]
    public async Task<IActionResult> Suspend(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new SuspendUserAccountCommand(id), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/reactivate")]
    [RequirePermission("users:manage")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ReactivateUserAccountCommand(id), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/sign-out")]
    [RequirePermission("users:manage")]
    public async Task<IActionResult> SignOutEverywhere(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new SignOutUserAccountCommand(id), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Every browser session and tray app this user is signed in on.</summary>
    [HttpGet("{id:guid}/sessions")]
    [RequirePermission("users:read")]
    public async Task<IActionResult> Sessions(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetUserAccountSessionsQuery(id), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>End one web session; the user's other sign-ins keep working.</summary>
    [HttpPost("{id:guid}/sessions/{sessionId:guid}/revoke")]
    [RequirePermission("users:manage")]
    public async Task<IActionResult> RevokeSession(Guid id, Guid sessionId, CancellationToken ct)
    {
        var result = await _mediator.Send(new RevokeUserAccountSessionCommand(id, sessionId, null), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Disconnect one tray app; the user's other sign-ins keep working.</summary>
    [HttpPost("{id:guid}/devices/{deviceId:guid}/revoke")]
    [RequirePermission("users:manage")]
    public async Task<IActionResult> RevokeDevice(Guid id, Guid deviceId, CancellationToken ct)
    {
        var result = await _mediator.Send(new RevokeUserAccountSessionCommand(id, null, deviceId), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
