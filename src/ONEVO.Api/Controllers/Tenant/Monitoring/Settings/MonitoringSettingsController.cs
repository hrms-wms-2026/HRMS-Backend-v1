using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Settings.ServiceInterfaces;

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
    private readonly ICurrentUser _currentUser;
    private readonly IMonitoringPolicyConfigurationService _configuration;

    public MonitoringSettingsController(
        IMediator mediator,
        ICurrentUser currentUser,
        IMonitoringPolicyConfigurationService configuration)
    {
        _mediator = mediator;
        _currentUser = currentUser;
        _configuration = configuration;
    }

    [HttpGet]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMonitoringFeatureTogglesQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("~/api/v1/attendance/monitoring/policy")]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> GetAttendanceMonitoringPolicy(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId == Guid.Empty)
            return Unauthorized();

        return Ok(await _configuration.GetAsync(_currentUser.TenantId, _currentUser.LegalEntityId, ct));
    }

    [HttpPut("~/api/v1/attendance/monitoring/policy/company")]
    [RequirePermission("monitoring:configure")]
    public Task<IActionResult> UpdateAttendanceMonitoringCompany(
        [FromBody] UpdateMonitoringFeatureTogglesRequest request, CancellationToken ct) => Update(request, ct);

    [HttpPut("~/api/v1/attendance/monitoring/policy/{scopeType}/{targetId:guid}")]
    [RequirePermission("monitoring:configure")]
    public async Task<IActionResult> UpsertAttendanceMonitoringOverride(
        string scopeType,
        Guid targetId,
        [FromBody] UpsertMonitoringPolicyOverrideRequest request,
        CancellationToken ct)
    {
        var result = await _configuration.UpsertOverrideAsync(
            _currentUser.TenantId,
            _currentUser.UserId,
            scopeType,
            targetId,
            _currentUser.LegalEntityId,
            new MonitoringPolicyOverrideRequest(
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
                request.Biometric,
                request.IdleThresholdMinutes,
                request.OverrideReason),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("~/api/v1/attendance/monitoring/policy/{scopeType}/{targetId:guid}")]
    [RequirePermission("monitoring:configure")]
    public async Task<IActionResult> DeleteAttendanceMonitoringOverride(
        string scopeType, Guid targetId, CancellationToken ct)
    {
        var result = await _configuration.DeleteOverrideAsync(
            _currentUser.TenantId, scopeType, targetId, _currentUser.LegalEntityId, ct);
        return result.IsSuccess
            ? NoContent()
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
                request.Biometric,
                request.IdleThresholdMinutes),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
