using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.CoreHr.Offboarding;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.SelectOffboardingChecklist;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.StartOffboarding;
using ONEVO.Application.Features.CoreHr.Offboarding.Queries.GetOffboarding;
using ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListOffboardingChecklistMatches;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

[ApiController]
[Route("api/v1/employees/{employeeId:guid}/offboarding")]
[Authorize(Policy = "TenantPolicy")]
public class EmployeeOffboardingController(IMediator mediator) : ControllerBase
{
    /// <summary>Step 1 - start an employee's offboarding. Fails 409 if one is already open.</summary>
    [HttpPost]
    [RequirePermission("employees:write")]
    [Idempotent]
    public async Task<IActionResult> Start(Guid employeeId, [FromBody] StartOffboardingRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new StartOffboardingCommand(employeeId, request.Reason, request.LastWorkingDate, request.KnowledgeRiskLevel, request.RehireEligibility, request.Notes), ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(Start), new { employeeId }, new { offboardingRecordId = result.Value })
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> Get(Guid employeeId, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetOffboardingQuery(employeeId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("checklist-matches")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetChecklistMatches(Guid employeeId, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListOffboardingChecklistMatchesQuery(employeeId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("select-checklist")]
    [RequirePermission("employees:write")]
    [Idempotent]
    public async Task<IActionResult> SelectChecklist(Guid employeeId, [FromBody] SelectOffboardingChecklistRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new SelectOffboardingChecklistCommand(employeeId, request.TemplateId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
