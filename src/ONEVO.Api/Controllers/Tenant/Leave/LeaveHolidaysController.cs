using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Leave.Holidays;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Leave.Calendar.Commands;

namespace ONEVO.Api.Controllers.Tenant.Leave;

[ApiController]
[Route("api/v1/leave/holidays")]
[Authorize(Policy = "TenantPolicy")]
public sealed class LeaveHolidaysController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveHolidaysController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequireAnyPermission("leave:manage", "calendar:read")]
    public async Task<IActionResult> List([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListLeaveHolidaysQuery(from, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Create([FromBody] CreateLeaveHolidayRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateLeaveHolidayCommand(request.Name, request.Date, request.EndDate), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteLeaveHolidayCommand(id), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
