using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.WorkLocation.Commands.ConfirmWorkLocation;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.WorkLocation;

/// <summary>Tray App → Backend: daily work-location confirmation.</summary>
[ApiController]
[Route("api/v1/monitoring/tray/work-location")]
[Authorize(Policy = "TrayDevicePolicy")]
public class TrayWorkLocationController : ControllerBase
{
    private readonly IMediator _mediator;

    public TrayWorkLocationController(IMediator mediator) => _mediator = mediator;

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(
        [FromBody] ConfirmWorkLocationRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new ConfirmWorkLocationCommand(
                request.LocationType, request.Latitude, request.Longitude, request.AccuracyMeters),
            ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Accepted();
    }
}

public record ConfirmWorkLocationRequest(
    [property: JsonPropertyName("location_type")] string LocationType,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude,
    [property: JsonPropertyName("accuracy_meters")] double? AccuracyMeters);
