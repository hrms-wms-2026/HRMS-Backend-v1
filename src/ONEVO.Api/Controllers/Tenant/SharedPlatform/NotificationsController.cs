using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.SharedPlatform.Notifications;
using ONEVO.Application.Features.SharedPlatform.Notifications.Commands.MarkAllNotificationsRead;
using ONEVO.Application.Features.SharedPlatform.Notifications.Commands.MarkNotificationRead;
using ONEVO.Application.Features.SharedPlatform.Notifications.Queries.GetMyNotifications;
using ONEVO.Application.Features.SharedPlatform.Notifications.Queries.GetUnreadCount;

namespace ONEVO.Api.Controllers.Tenant.SharedPlatform;

[ApiController]
[Route("api/v1/notifications")]
[Authorize(Policy = "TenantPolicy")]
public class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public NotificationsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> GetMine([FromQuery] bool unreadOnly = false, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMyNotificationsQuery(unreadOnly, page), ct);
        return result.IsSuccess
            ? Ok(result.Value!.Select(n => n.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetUnreadCountQuery(), ct);
        return result.IsSuccess
            ? Ok(new { count = result.Value })
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new MarkNotificationReadCommand(id), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        var result = await _mediator.Send(new MarkAllNotificationsReadCommand(), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
