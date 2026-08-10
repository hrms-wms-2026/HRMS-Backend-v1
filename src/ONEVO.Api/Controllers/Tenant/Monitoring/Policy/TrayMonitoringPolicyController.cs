using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.Policy.Queries.GetEffectiveTrayPolicy;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.Policy;

/// <summary>
/// Effective monitoring policy for the tray Agent Service, polled to learn which
/// monitoring capabilities are currently enabled for the calling employee/tenant.
/// </summary>
[ApiController]
[Route("api/v1/monitoring/tray/policy")]
[Authorize(Policy = "TrayDevicePolicy")]
public class TrayMonitoringPolicyController : ControllerBase
{
    private readonly IMediator _mediator;

    public TrayMonitoringPolicyController(IMediator mediator)
        => _mediator = mediator;

    /// <summary>
    /// Get the effective monitoring policy for the authenticated tray device.
    /// Employee and tenant identity are read from the Device JWT only — never
    /// from query or body data.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetEffectivePolicy(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetEffectiveTrayPolicyQuery(), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }
}
