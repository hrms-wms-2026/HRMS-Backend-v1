using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.Policy.Queries.GetEffectiveTrayPolicy;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.Policy;

/// <summary>
/// Effective monitoring policy for the authenticated tray device.
/// Authorization: Bearer {tray_access_token}
/// </summary>
[ApiController]
[Route("api/v1/monitoring/tray")]
[Authorize(Policy = "TrayDevicePolicy")]
public sealed class TrayMonitoringPolicyController : ControllerBase
{
    private readonly IMediator _mediator;

    public TrayMonitoringPolicyController(IMediator mediator)
        => _mediator = mediator;

    /// <summary>
    /// Returns the versioned Agent policy for this device/employee.
    /// Tenant and employee identity come only from the tray JWT — never from query or body.
    /// </summary>
    [HttpGet("policy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetEffectivePolicy(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetEffectiveTrayPolicyQuery(), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }
}
