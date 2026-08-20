using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.Sprints;
using ONEVO.Api.Contracts.WorkManagement.Tasks;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.AchieveSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CreateSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.EditSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.SetSprintStatus;
using ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetObjectiveSprints;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetSprintTasks;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

[ApiController]
[Route("api/v1/work")]
[Authorize(Policy = "TenantPolicy")]
public class SprintsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SprintsController(IMediator mediator) => _mediator = mediator;

    [HttpPost("objectives/{objectiveId:guid}/sprints")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Create(Guid objectiveId, [FromBody] CreateSprintRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateSprintCommand(objectiveId, request.Name, request.StartDate, request.EndDate), ct);

        return result.IsSuccess
            ? StatusCode(201, result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("sprints/{id:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Edit(Guid id, [FromBody] EditSprintRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new EditSprintCommand(id, request.Name, request.StartDate, request.EndDate), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("sprints/{id:guid}/complete")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new CompleteSprintCommand(id), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("sprints/{id:guid}/achieve")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Achieve(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new AchieveSprintCommand(id), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("sprints/{id:guid}/status")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetSprintStatusRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new SetSprintStatusCommand(id, request.Status), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("sprints/{id:guid}/tasks")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> GetTasks(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSprintTasksQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(t => t.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("objectives/{objectiveId:guid}/sprints")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> GetByObjective(Guid objectiveId, [FromQuery] bool activeOnly, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetObjectiveSprintsQuery(objectiveId, activeOnly), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(s => s.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
