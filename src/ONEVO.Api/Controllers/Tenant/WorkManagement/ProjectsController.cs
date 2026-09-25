using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.Objectives;
using ONEVO.Api.Contracts.WorkManagement.Projects;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.AchieveProject;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.AddProjectMember;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.CreateProject;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.DeleteProject;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.EditProject;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.UnachieveProject;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Requests;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectBanner;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectById;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectLogo;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.ListProjects;
using ONEVO.Application.Features.WorkManagement.Approvals.Queries.GetWorkApprovalHistory;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

[ApiController]
[Route("api/v1/work/projects")]
[Authorize(Policy = "TenantPolicy")]
[RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
public class ProjectsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProjectsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Lists approval requests in this project that the caller sent, currently needs to decide,
    /// or previously approved/rejected.
    /// </summary>
    [HttpGet("{id:guid}/approval-history")]
    public async Task<IActionResult> ApprovalHistory(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetWorkApprovalHistoryQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

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

    /// <summary>Soft-deletes a Project (is_active = false). Only the project lead may delete. Already-deleted returns 409.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteProjectCommand(id), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Marks a Project Achieved. Requires every top-level milestone (direct child of the Default Objective) to already be Achieved. Lead-only, always immediate - the Project is the tree's root, no approval routing.</summary>
    [HttpPost("{id:guid}/achieve")]
    public async Task<IActionResult> Achieve(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new AchieveProjectCommand(id), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Reverts an Achieved Project back to active. Lead-only, always immediate.</summary>
    [HttpPost("{id:guid}/unachieve")]
    public async Task<IActionResult> Unachieve(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new UnachieveProjectCommand(id), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Gets a single Project by id. The handler requires an active project relationship.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProjectByIdQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Streams a Project's cover/logo image. Requires active project membership. 404 if no logo is set.</summary>
    [HttpGet("{id:guid}/logo")]
    public async Task<IActionResult> GetLogo(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProjectLogoQuery(id), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return File(result.Value!.Content, result.Value!.ContentType);
    }

    /// <summary>Streams a Project's banner image. Requires active project membership. 404 if no banner is set.</summary>
    [HttpGet("{id:guid}/banner")]
    public async Task<IActionResult> GetBanner(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProjectBannerQuery(id), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return File(result.Value!.Content, result.Value!.ContentType);
    }

    /// <summary>Invites an employee to this project via its Default Objective. Project-owner (LeadId) only. Immediate no-op (204) if already an active member of the Default Objective; otherwise creates a pending invitation (202) the invited employee must accept.</summary>
    [HttpPost("{id:guid}/members")]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddProjectMemberRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new AddProjectMemberCommand(id, request.EmployeeId), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return result.Value!.AlreadyMember
            ? StatusCode(204, result.Value.ToViewModel())
            : StatusCode(202, result.Value.ToViewModel());
    }

    /// <summary>Lists the caller's own projects from active project relationships.</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> ListMine([FromQuery] PagedRequest paging, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListProjectsQuery(null, paging), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Legacy route retained for compatibility. The handler rejects requests for another employee.</summary>
    [HttpGet]
    public async Task<IActionResult> ListByUser([FromQuery] Guid employeeId, [FromQuery] PagedRequest paging, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListProjectsQuery(employeeId, paging), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
