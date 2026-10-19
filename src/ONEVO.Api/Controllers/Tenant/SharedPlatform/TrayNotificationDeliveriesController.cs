using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.SharedPlatform.Notifications.Tray.Commands.AckTrayDelivery;
using ONEVO.Application.Features.SharedPlatform.Notifications.Tray.Queries.GetPendingTrayDeliveries;

namespace ONEVO.Api.Controllers.Tenant.SharedPlatform;

/// <summary>
/// Generic notification transport for tray clients that declare support for it by calling these routes.
/// Legacy clients keep using /monitoring/tray/notifications/pending, which is unchanged.
/// </summary>
[ApiController]
[Route("api/v1/monitoring/tray/notifications/deliveries")]
[Authorize(Policy = "TrayDevicePolicy")]
public sealed class TrayNotificationDeliveriesController : ControllerBase
{
    private readonly IMediator _mediator;

    public TrayNotificationDeliveriesController(IMediator mediator) => _mediator = mediator;

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPendingTrayDeliveriesQuery(), ct);
        return result.IsSuccess ? Ok(new { deliveries = result.Value }) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{deliveryId:guid}/ack")]
    public async Task<IActionResult> Ack(Guid deliveryId, CancellationToken ct)
    {
        var result = await _mediator.Send(new AckTrayDeliveryCommand(deliveryId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
