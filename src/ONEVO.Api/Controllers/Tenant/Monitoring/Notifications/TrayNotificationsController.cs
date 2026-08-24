using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.Notifications.Commands.AckTrayNotification;
using ONEVO.Application.Features.Monitoring.Notifications.Queries.GetPendingTrayNotifications;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.Notifications;

/// <summary>Tray App → Backend polling for break/idle wellness notifications. Mirrors the shape of the tray-command endpoints.</summary>
[ApiController]
[Route("api/v1/monitoring/tray/notifications")]
[Authorize(Policy = "TrayDevicePolicy")]
public class TrayNotificationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public TrayNotificationsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPendingTrayNotificationsQuery(), ct);
        return result.IsSuccess ? Ok(new { notifications = result.Value }) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{notificationId:guid}/ack")]
    public async Task<IActionResult> Ack(Guid notificationId, CancellationToken ct)
    {
        var result = await _mediator.Send(new AckTrayNotificationCommand(notificationId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
