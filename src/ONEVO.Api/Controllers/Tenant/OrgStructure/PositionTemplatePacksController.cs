using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.OrgStructure.PositionTemplatePacks.Queries.ListPositionTemplatePacks;

namespace ONEVO.Api.Controllers.Tenant.OrgStructure;

/// <summary>Tenant-facing read of active system Position Template Packs for the Position screen
/// template picker. Read-only foundation: does not create tenant departments/positions and does
/// not write tenant_configuration_template_applications. Distinct from the Developer
/// Platform/admin-only /admin/v1/configuration-templates catalog.</summary>
[ApiController]
[Route("api/v1/org/position-template-packs")]
[Authorize(Policy = "TenantPolicy")]
public class PositionTemplatePacksController : ControllerBase
{
    private readonly IMediator _mediator;

    public PositionTemplatePacksController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>List active system Position Template Packs available to this tenant.</summary>
    [HttpGet]
    [RequirePermission("org:read")]
    public async Task<IActionResult> List(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListPositionTemplatePacksQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
