using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Planner.Commands;
using ONEVO.Application.Features.WorkManagement.Planner.Contracts;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

/// <summary>Day planner (2026-10-26 design, Part B; 2026-10-27 Planner = Calendar; docs/api/planner-v1.md). Writes touch the
/// caller's own plan, today up to 28 days ahead.</summary>
[ApiController]
[Route("api/v1/planner")]
[Authorize(Policy = "TenantPolicy")]
public sealed class PlannerController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlannerController(IMediator mediator) => _mediator = mediator;

    [HttpGet("me")]
    public async Task<IActionResult> MyDay([FromQuery] DateOnly? date, CancellationToken ct)
        => Respond(await _mediator.Send(new GetMyPlannerDayQuery(date), ct));

    [HttpGet("employees/{employeeId:guid}")]
    public async Task<IActionResult> EmployeeDay(Guid employeeId, [FromQuery] DateOnly? date, CancellationToken ct)
        => Respond(await _mediator.Send(new GetEmployeePlannerDayQuery(employeeId, date), ct));

    [HttpGet("me/range")]
    public async Task<IActionResult> MyRange([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
        => Respond(await _mediator.Send(new GetMyPlannerRangeQuery(from, to), ct));

    [HttpPost("me/blocks")]
    public async Task<IActionResult> CreateBlock([FromBody] CreatePlannedBlockRequest request, CancellationToken ct)
        => Respond(await _mediator.Send(new CreatePlannedBlockCommand(request), ct));

    [HttpPatch("me/blocks/{blockId:guid}")]
    public async Task<IActionResult> MoveBlock(Guid blockId, [FromBody] MovePlannedBlockRequest request, CancellationToken ct)
        => Respond(await _mediator.Send(new MovePlannedBlockCommand(blockId, request), ct));

    [HttpDelete("me/blocks/{blockId:guid}")]
    public async Task<IActionResult> DeleteBlock(Guid blockId, CancellationToken ct)
        => Respond(await _mediator.Send(new DeletePlannedBlockCommand(blockId), ct));

    private IActionResult Respond<T>(Result<T> result)
    {
        if (result.IsSuccess)
            return Ok(result.Value);
        var problem = new ProblemDetails
        {
            Status = result.StatusCode ?? StatusCodes.Status400BadRequest,
            Title = "The planner request could not be completed.",
            Detail = result.Error,
        };
        if (!string.IsNullOrWhiteSpace(result.ErrorCode))
            problem.Extensions["code"] = result.ErrorCode;
        return new ObjectResult(problem) { StatusCode = problem.Status };
    }
}
