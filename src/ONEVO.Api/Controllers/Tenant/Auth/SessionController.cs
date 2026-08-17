using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Auth.Session;
using ONEVO.Application.Features.Auth.ActiveCompany.Commands.SwitchActiveCompany;

namespace ONEVO.Api.Controllers.Tenant.Auth;

[ApiController]
[Route("api/v1/session")]
[Authorize(Policy = "TenantPolicy")]
public class SessionController : ControllerBase
{
    private readonly IMediator _mediator;

    public SessionController(IMediator mediator) => _mediator = mediator;

    /// <summary>Switch which of the caller's own Employee rows (company/legal entity) is active
    /// for this session. Permissions reflect the new active company on the next request.</summary>
    [HttpPost("active-company")]
    public async Task<IActionResult> SwitchActiveCompany(
        [FromBody] SwitchActiveCompanyRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new SwitchActiveCompanyCommand(request.EmployeeId), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
