using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Monitoring.Settings;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Monitoring.Settings.Commands.UpdateMonitoringFeatureToggles;
using ONEVO.Application.Features.Monitoring.Settings.Queries.GetMonitoringFeatureToggles;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.Settings;

/// <summary>
/// Tenant admin CRUD for tenant-level monitoring capability toggles.
/// Without this endpoint, MonitoringFeatureToggles has no write path and every
/// monitoring capability permanently resolves to false in production tenants
/// (see MonitoringToggleResolverService and DevSmokeTestTenantSeeder's dev-only workaround).
/// </summary>
[ApiController]
[Route("api/v1/monitoring/settings")]
[Authorize(Policy = "TenantPolicy")]
public class MonitoringSettingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringSettingsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMonitoringFeatureTogglesQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut]
    [RequirePermission("monitoring:configure")]
    public async Task<IActionResult> Update(
        [FromBody] UpdateMonitoringFeatureTogglesRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new UpdateMonitoringFeatureTogglesCommand(
                request.ActivityMonitoring,
                request.ApplicationTracking,
                request.DocumentTracking,
                request.CommunicationTracking,
                request.ScreenshotCapture,
                request.AutoScreenshotCapture,
                request.MeetingDetection,
                request.DeviceTracking,
                request.WorkLocationVerification,
                request.IdentityVerification,
                request.Biometric),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
