using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.Projects;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.CreateProject;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Requests;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

[ApiController]
[Route("api/v1/work/projects")]
[Authorize(Policy = "TenantPolicy")]
public class ProjectsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProjectsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Creates a Project with its Default Objective, creator membership, Default Version, release reminder, optional labels, and optional logo — all in one atomic transaction.</summary>
    [HttpPost]
    [RequirePermission("projects:create")]
    [Idempotent]
    public async Task<IActionResult> Create([FromForm] CreateProjectFormRequest request, CancellationToken ct)
    {
        var labels = string.IsNullOrWhiteSpace(request.LabelsJson)
            ? new List<CreateProjectLabelInput>()
            : JsonSerializer.Deserialize<List<CreateProjectLabelInput>>(
                request.LabelsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        Stream? logoStream = null;
        if (request.Logo is { Length: > 0 } logo)
            logoStream = logo.OpenReadStream();

        var command = new CreateProjectCommand(
            request.CategoryId,
            request.Name,
            request.Identifier,
            request.Description,
            request.StartDate,
            request.TargetDate,
            request.ReleaseDate,
            request.Color,
            request.ActualHours,
            request.DefaultObjectiveAllocatedHours,
            labels,
            request.Logo?.FileName,
            request.Logo?.ContentType,
            logoStream);

        var result = await _mediator.Send(command, ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Project.Id }, result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Placeholder route target for the Create action's Location header. The real GET-by-id read endpoint is built in Slice 2; this action only exists so CreatedAtAction can resolve a route name — it returns 501 until Slice 2 lands.</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("projects:read")]
    public IActionResult GetById(Guid id) => StatusCode(501);
}
