using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.WeeklyProgress.DTOs;
using ONEVO.Application.Features.WorkManagement.WeeklyProgress.Queries.GetMyWeeklyWorkProgress;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

/// <summary>The caller's own work progress (Weekly Work Progress widget, 2026-10-28 design). Same gates as TasksController.</summary>
[ApiController]
[Route("api/v1/work")]
[Authorize(Policy = "TenantPolicy")]
[RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
public sealed class MyWorkProgressController(IMediator mediator) : ControllerBase
{
    /// <summary>This week (or the Monday given) and the week before, for the caller.</summary>
    [HttpGet("my-weekly-progress")]
    public async Task<IActionResult> MyWeeklyProgress([FromQuery] DateOnly? weekStart, CancellationToken ct = default)
        => Respond(await mediator.Send(new GetMyWeeklyWorkProgressQuery(weekStart), ct));

    private IActionResult Respond(Result<WeeklyWorkProgressResponse> result)
    {
        if (result.IsSuccess)
            return Ok(result.Value);
        var problem = new ProblemDetails
        {
            Status = result.StatusCode ?? StatusCodes.Status400BadRequest,
            Title = "The weekly progress request could not be completed.",
            Detail = result.Error,
        };
        if (!string.IsNullOrWhiteSpace(result.ErrorCode))
            problem.Extensions["code"] = result.ErrorCode;
        return new ObjectResult(problem) { StatusCode = problem.Status };
    }
}
