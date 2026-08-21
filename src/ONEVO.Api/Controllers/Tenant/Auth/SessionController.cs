using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Auth.Session;
using ONEVO.Application.Features.Auth.ActiveCompany.Commands.SwitchActiveCompany;
using ONEVO.Application.Features.OrgStructure.Queries.ListLegalEntities;

namespace ONEVO.Api.Controllers.Tenant.Auth;

[ApiController]
[Route("api/v1/session")]
[Authorize(Policy = "TenantPolicy")]
public class SessionController : ControllerBase
{
    private readonly IMediator _mediator;

    public SessionController(IMediator mediator) => _mediator = mediator;

    /// <summary>Lists the active companies accessible to the current tenant user. This is a
    /// session capability and deliberately does not require Organization structure permissions.</summary>
    [HttpGet("companies")]
    public async Task<IActionResult> ListCompanies(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListLegalEntitiesQuery(IncludeInactive: false), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Switch to the selected legal entity through the caller's active Employee row.
    /// Permissions reflect the new active company on the next request.</summary>
    [HttpPost("active-company")]
    public async Task<IActionResult> SwitchActiveCompany(
        [FromBody] SwitchActiveCompanyRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new SwitchActiveCompanyCommand(request.LegalEntityId), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
