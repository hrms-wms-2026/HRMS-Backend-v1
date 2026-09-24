using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.Notifications.Commands.MarkNotificationRead;
using ONEVO.Application.Features.Monitoring.Notifications.Queries.GetNotificationInbox;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.Notifications;

[ApiController]
[Route("api/v1/monitoring/notifications")]
[Authorize(Policy = "TenantPolicy")]
public class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public NotificationsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> GetInbox([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetNotificationInboxQuery { Page = page, PageSize = pageSize }, ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{notificationId:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid notificationId, CancellationToken ct)
    {
        var result = await _mediator.Send(new MarkNotificationReadCommand(notificationId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
