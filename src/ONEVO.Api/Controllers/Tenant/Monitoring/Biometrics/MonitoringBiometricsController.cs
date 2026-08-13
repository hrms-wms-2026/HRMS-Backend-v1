using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.Biometrics.Commands.CompleteEnrollmentAttempt;
using ONEVO.Application.Features.Monitoring.Biometrics.Commands.CreateEnrollmentAttempt;
using ONEVO.Application.Features.Monitoring.Biometrics.Queries.GetBiometricProfile;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.Biometrics;

[ApiController]
[Route("api/v1/monitoring/biometrics")]
[Authorize(Policy = "TrayDevicePolicy")]
public class MonitoringBiometricsController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringBiometricsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Creates a new AWS Face Liveness enrollment session.
    /// Authorization: Bearer {tray_access_token}
    /// </summary>
    [HttpPost("enrollment-attempts")]
    public async Task<IActionResult> CreateEnrollmentAttempt(CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateEnrollmentAttemptCommand(), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    /// <summary>
    /// Completes an enrollment attempt after the WebView2 liveness capture finished.
    /// Authorization: Bearer {tray_access_token}
    /// </summary>
    [HttpPost("enrollment-attempts/{id:guid}/complete")]
    public async Task<IActionResult> CompleteEnrollmentAttempt(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new CompleteEnrollmentAttemptCommand(id), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    /// <summary>
    /// Returns the current employee's active biometric enrollment status, if any.
    /// Authorization: Bearer {tray_access_token}
    /// </summary>
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetBiometricProfileQuery(), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }
}
