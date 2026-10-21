using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Queries;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

/// <summary>
/// The reporting-line attendance view on the employee profile (spec D16, D17):
/// <c>GET /api/v1/employees/{employeeId}/meeting-attendance</c>.
/// <para>
/// Who may see it is decided in the application layer - the employee themself, or a manager in their current
/// reporting line - so there is no permission attribute here, and "not found" and "not allowed to see" share one
/// 404 body. Every failure carries a stable <c>code</c> extension.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/employees")]
[Authorize(Policy = "TenantPolicy")]
public sealed class EmployeeMeetingAttendanceController : ControllerBase
{
    private readonly IMediator _mediator;

    public EmployeeMeetingAttendanceController(IMediator mediator) => _mediator = mediator;

    /// <summary>The employee's meeting attendance over <paramref name="from"/>..<paramref name="to"/> (default: the last
    /// 30 days, at most 92).</summary>
    [HttpGet("{employeeId:guid}/meeting-attendance")]
    public async Task<IActionResult> Get(Guid employeeId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        CancellationToken ct)
        => Respond(await _mediator.Send(new EmployeeMeetingAttendanceQuery(employeeId, from, to), ct), Ok);

    private IActionResult Respond<T>(Result<T> result, Func<T, IActionResult> onSuccess)
    {
        if (result.IsSuccess)
            return onSuccess(result.Value!);
        var problem = new ProblemDetails
        {
            Status = result.StatusCode ?? StatusCodes.Status400BadRequest,
            Title = "The meeting attendance request could not be completed.",
            Detail = result.Error,
        };
        if (!string.IsNullOrWhiteSpace(result.ErrorCode))
            problem.Extensions["code"] = result.ErrorCode;
        return new ObjectResult(problem) { StatusCode = problem.Status };
    }
}
