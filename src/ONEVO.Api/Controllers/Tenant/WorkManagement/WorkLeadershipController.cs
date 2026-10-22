using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedEmployeeHours;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedPeriodProgress;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedProgressTasks;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWeeklyCompletion;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWorkProgress;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

/// <summary>My Team - Work I Lead (spec §8.3). Relationship-based like the rest of Work
/// Management: the module gate only, no RequirePermission; the handler computes ownership.</summary>
[ApiController]
[Route("api/v1/work")]
[Authorize(Policy = "TenantPolicy")]
[RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
public sealed class WorkLeadershipController(IMediator mediator) : ControllerBase
{
    [HttpGet("led-progress")]
    public async Task<IActionResult> LedProgress([FromQuery] int overdueLimit = 10, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetLedWorkProgressQuery(overdueLimit), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Team Progress detail: per-employee estimated hours in the caller's led scope, plus
    /// the unassigned total. A shared task counts in full for each assignee.</summary>
    [HttpGet("led-progress/employees")]
    public async Task<IActionResult> LedProgressEmployees(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] Guid? projectId = null,
        [FromQuery] Guid? objectiveId = null,
        [FromQuery] string? sort = "totalHours",
        [FromQuery] string? direction = "desc",
        [FromQuery] string? period = "all",
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new GetLedEmployeeHoursQuery(pageNumber, pageSize, search, projectId, objectiveId, sort, direction, period), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Team Progress detail: the top-level tasks behind the hours in the caller's led scope.</summary>
    [HttpGet("led-progress/tasks")]
    public async Task<IActionResult> LedProgressTasks(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] Guid? projectId = null,
        [FromQuery] Guid? objectiveId = null,
        [FromQuery] Guid? employeeId = null,
        [FromQuery] string? sort = "estimatedHours",
        [FromQuery] string? direction = "desc",
        [FromQuery] string? period = "all",
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new GetLedProgressTasksQuery(pageNumber, pageSize, search, status, projectId, objectiveId, employeeId, sort, direction, period), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Team Progress period switch: totals of the led-scope tasks due this week (ISO,
    /// Monday..Sunday) or this calendar month, from today's UTC date. period=week|month only.</summary>
    [HttpGet("led-progress/period")]
    public async Task<IActionResult> LedProgressPeriod(
        [FromQuery] string? period = null,
        [FromQuery] Guid? projectId = null,
        [FromQuery] Guid? objectiveId = null,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetLedPeriodProgressQuery(period, projectId, objectiveId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Team Progress weekly-completion chart: estimated hours of the led-scope tasks
    /// completed in each of the last <paramref name="weeks"/> ISO weeks (UTC, oldest first, current
    /// week last), the done tasks with no completion date, and the scope's allocation. weeks=1..26.</summary>
    [HttpGet("led-progress/weekly-completion")]
    public async Task<IActionResult> LedProgressWeeklyCompletion(
        [FromQuery] int weeks = 10,
        [FromQuery] Guid? projectId = null,
        [FromQuery] Guid? objectiveId = null,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetLedWeeklyCompletionQuery(weeks, projectId, objectiveId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
