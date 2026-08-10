using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.Projects;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.ListProjectCategories;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

[ApiController]
[Route("api/v1/work/project-categories")]
[Authorize(Policy = "TenantPolicy")]
public class ProjectCategoriesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProjectCategoriesController(IMediator mediator) => _mediator = mediator;

    /// <summary>Lists the tenant's Project Categories, active-only by default. Used to populate the category picker on Create/Edit Project and the Project list's category filter.</summary>
    [HttpGet]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListProjectCategoriesQuery(includeInactive), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(c => c.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
