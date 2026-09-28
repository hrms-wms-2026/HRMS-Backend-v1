using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.EnrollFacePhotos;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.SubmitCheckIn;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.UploadFaceScan;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.ValidateFacePhoto;
using ONEVO.Application.Features.Monitoring.CheckIn.Queries.GetFaceReferenceStatus;

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
    /// Preview a clock-in/out selfie against AWS DetectFaces + CompareFaces without
    /// creating a check-in. The tray uses this to gate Clock In: pass → proceed, fail → retake.
    /// Accepts multipart/form-data with a "face_scan" file field, an optional "purpose"
    /// (enrollment | clock_in | clock_out) and, for enrollment, the face setup "pose"
    /// (front | left | right). Nothing is saved here — face setup saves via face-enroll.
    /// Authorization: Bearer {tray_access_token}
    /// </summary>
    [HttpPost("face-preview")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> ValidateFacePhoto(
        IFormFile face_scan,
        [FromForm(Name = "purpose")] string? purpose,
        [FromForm(Name = "pose")] string? pose,
        CancellationToken ct)
    {
        if (face_scan is null || face_scan.Length == 0)
            return Problem("face_scan file is required.", statusCode: 400);

        await using var stream = face_scan.OpenReadStream();
        var result = await _mediator.Send(new ValidateFacePhotoCommand(
            stream,
            face_scan.ContentType,
            face_scan.Length,
            string.IsNullOrWhiteSpace(purpose) ? null : purpose.Trim(),
            string.IsNullOrWhiteSpace(pose) ? null : pose.Trim()), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    /// <summary>
    /// Whether this device's employee already has an enrolled face. Tray device setup skips the
    /// face setup screen when they do; clock-in still verifies against that face every time.
    /// Authorization: Bearer {tray_access_token}
    /// </summary>
    [HttpGet("face-reference")]
    public async Task<IActionResult> GetFaceReferenceStatus(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetFaceReferenceStatusQuery(), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    /// <summary>
    /// Tray face setup: saves the look-straight, turned-left and turned-right photos together as
    /// the employee's reference faces. Accepts multipart/form-data with "front", "left" and
    /// "right" file fields. Refused ("already_enrolled") when a reference already exists.
    /// Authorization: Bearer {tray_access_token}
    /// </summary>
    [HttpPost("face-enroll")]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<IActionResult> EnrollFacePhotos(
        IFormFile front,
        IFormFile left,
        IFormFile right,
        CancellationToken ct)
    {
        if (front is null || front.Length == 0 || left is null || left.Length == 0 || right is null || right.Length == 0)
            return Problem("front, left and right photos are required.", statusCode: 400);

        await using var frontStream = front.OpenReadStream();
        await using var leftStream = left.OpenReadStream();
        await using var rightStream = right.OpenReadStream();
        var result = await _mediator.Send(new EnrollFacePhotosCommand(
            new FaceSetupPhoto(frontStream, front.ContentType, front.Length),
            new FaceSetupPhoto(leftStream, left.ContentType, left.Length),
            new FaceSetupPhoto(rightStream, right.ContentType, right.Length)), ct);

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
