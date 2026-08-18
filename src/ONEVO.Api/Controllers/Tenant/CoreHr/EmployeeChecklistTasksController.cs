using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.CoreHr.Offboarding;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.UpdateEmployeeChecklistTask;
using ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListEmployeeChecklistTasks;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

[ApiController]
[Route("api/v1/employees/{employeeId:guid}/checklist-tasks")]
[Authorize(Policy = "TenantPolicy")]
public class EmployeeChecklistTasksController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> List(Guid employeeId, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListEmployeeChecklistTasksQuery(employeeId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("{taskId:guid}")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> Update(Guid employeeId, Guid taskId, [FromBody] UpdateEmployeeChecklistTaskRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new UpdateEmployeeChecklistTaskCommand(employeeId, taskId, request.AssignedToId, request.DueDate, request.IsRequired), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
