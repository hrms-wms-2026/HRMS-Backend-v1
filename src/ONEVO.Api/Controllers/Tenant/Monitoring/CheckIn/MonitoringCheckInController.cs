using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.SubmitCheckIn;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.UploadFaceScan;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.CheckIn;

[ApiController]
[Route("api/v1/monitoring/check-in")]
[Authorize(Policy = "TrayDevicePolicy")]
public class MonitoringCheckInController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringCheckInController(IMediator mediator)
        => _mediator = mediator;

    /// <summary>
    /// Submit a check-in with location and device serial number.
    /// Called by the tray app immediately on check-in action.
    /// Authorization: Bearer {tray_access_token}
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SubmitCheckIn(
        [FromBody] SubmitCheckInRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new SubmitCheckInCommand(
            request.Latitude,
            request.Longitude,
            request.LocationAccuracy,
            request.LocationAddress,
            request.DeviceSerialNumber), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    /// <summary>
    /// Upload a face scan photo for a previously submitted check-in.
    /// Accepts multipart/form-data with a single "face_scan" file field.
    /// Authorization: Bearer {tray_access_token}
    /// </summary>
    [HttpPost("{checkInId:guid}/face-scan")]
    [RequestSizeLimit(6 * 1024 * 1024)] // 6 MB limit (5 MB image + overhead)
    public async Task<IActionResult> UploadFaceScan(
        Guid checkInId,
        IFormFile face_scan,
        CancellationToken ct)
    {
        if (face_scan is null || face_scan.Length == 0)
            return Problem("face_scan file is required.", statusCode: 400);

        await using var stream = face_scan.OpenReadStream();
        var result = await _mediator.Send(new UploadFaceScanCommand(
            checkInId,
            stream,
            face_scan.ContentType,
            face_scan.Length), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }
}

public record SubmitCheckInRequest(
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude,
    [property: JsonPropertyName("location_accuracy")] double? LocationAccuracy,
    [property: JsonPropertyName("location_address")] string? LocationAddress,
    [property: JsonPropertyName("device_serial_number")] string? DeviceSerialNumber);
