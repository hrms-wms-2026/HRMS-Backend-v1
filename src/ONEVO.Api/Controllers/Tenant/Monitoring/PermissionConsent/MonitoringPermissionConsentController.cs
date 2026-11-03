using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.PermissionConsent.Commands.RecordPermissionConsent;
using ONEVO.Application.Features.Monitoring.PermissionConsent.Queries.GetPermissionConsentStatus;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.PermissionConsent;

[ApiController]
[Route("api/v1/monitoring/permission-consent")]
[Authorize(Policy = "TrayDevicePolicy")]
public class MonitoringPermissionConsentController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringPermissionConsentController(IMediator mediator)
        => _mediator = mediator;

    /// <summary>
    /// Whether this device's employee already agreed to the privacy notice and allowed the
    /// tray's permissions, with the toggles from their latest acceptance. Tray device setup
    /// skips the Privacy and Permissions screens when the acceptance covers the current policy.
    /// Authorization: Bearer {tray_access_token}
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPermissionConsentStatusQuery(), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    /// <summary>
    /// Records that the employee agreed and tapped "Allow &amp; Continue" with these toggles.
    /// Authorization: Bearer {tray_access_token}
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Record(
        [FromBody] RecordPermissionConsentRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new RecordPermissionConsentCommand(
            request.ScreenMonitoring,
            request.AppTracking,
            request.LocationAccess,
            request.CameraAccess,
            request.Notifications,
            request.KeyboardMouse), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }
}

public record RecordPermissionConsentRequest(
    [property: JsonPropertyName("screen_monitoring")] bool ScreenMonitoring,
    [property: JsonPropertyName("app_tracking")] bool AppTracking,
    [property: JsonPropertyName("location_access")] bool LocationAccess,
    [property: JsonPropertyName("camera_access")] bool CameraAccess,
    [property: JsonPropertyName("notifications")] bool Notifications,
    [property: JsonPropertyName("keyboard_mouse")] bool KeyboardMouse);
