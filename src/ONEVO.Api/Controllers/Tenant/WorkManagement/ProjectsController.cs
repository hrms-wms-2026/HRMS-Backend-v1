using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.Projects;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.AchieveProject;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.CreateProject;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.DeleteProject;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.EditProject;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.UnachieveProject;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Requests;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectBanner;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectById;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectLogo;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.ListProjects;

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
    [RequirePermission("projects:access")]
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

        Stream? bannerStream = null;
        if (request.Banner is { Length: > 0 } banner)
            bannerStream = banner.OpenReadStream();

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
            logoStream,
            request.Banner?.FileName,
            request.Banner?.ContentType,
            bannerStream);

        var result = await _mediator.Send(command, ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Project.Id }, result.Value.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Updates a Project's editable fields (name, description, category, dates, color, actual hours, optional allocated hours). Cascades title/description/dates onto the Default Objective; allocated hours also cascade when provided.</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Edit(Guid id, [FromBody] EditProjectRequest request, CancellationToken ct)
    {
        var command = new EditProjectCommand(
            id, request.Name, request.Description, request.CategoryId,
            request.StartDate, request.TargetDate, request.Color, request.ActualHours, request.Identifier,
            request.AllocatedHours);

        var result = await _mediator.Send(command, ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Soft-deletes a Project (is_active = false). Only the project lead may delete, even with projects:access. Already-deleted returns 409.</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteProjectCommand(id), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Marks a Project Achieved. Requires every top-level milestone (direct child of the Default Objective) to already be Achieved. Lead-only, always immediate - the Project is the tree's root, no approval routing.</summary>
    [HttpPost("{id:guid}/achieve")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Achieve(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new AchieveProjectCommand(id), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Reverts an Achieved Project back to active. Lead-only, always immediate.</summary>
    [HttpPost("{id:guid}/unachieve")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Unachieve(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new UnachieveProjectCommand(id), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Gets a single Project by id. No [RequirePermission] here on purpose: access is granted by projects:read/* OR by having an active project_members row for this project — the handler checks both, since the attribute alone would hard-block members who lack the tenant-wide permission.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProjectByIdQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Streams a Project's cover/logo image. Same access rule as GetById (projects:read/* OR active membership) so the image is never more visible than the project itself. 404 if no logo is set.</summary>
    [HttpGet("{id:guid}/logo")]
    public async Task<IActionResult> GetLogo(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProjectLogoQuery(id), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return File(result.Value!.Content, result.Value!.ContentType);
    }

    /// <summary>Streams a Project's banner image. Same access rule as GetById (projects:read/* OR active membership) so the image is never more visible than the project itself. 404 if no banner is set.</summary>
    [HttpGet("{id:guid}/banner")]
    public async Task<IActionResult> GetBanner(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProjectBannerQuery(id), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return File(result.Value!.Content, result.Value!.ContentType);
    }

    /// <summary>The caller's own projects. Requires projects:access (the module-wide base gate) — this only ever returns the caller's own data, so no additional permission is needed beyond that base gate.</summary>
    [HttpGet("mine")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> ListMine([FromQuery] PagedRequest paging, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListProjectsQuery(null, paging), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Any given employee's projects (admin/company-owner path). If employeeId doesn't resolve to an employee with any active membership, returns an empty page, not 404 — list semantics. projects:read is unchanged by the 2026-08-04 permission-model update (it stays the sole "view others" gate); role configuration is expected to grant projects:access alongside it, not enforced here as a second attribute check.</summary>
    [HttpGet]
    [RequirePermission("projects:read")]
    public async Task<IActionResult> ListByUser([FromQuery] Guid employeeId, [FromQuery] PagedRequest paging, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListProjectsQuery(employeeId, paging), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
