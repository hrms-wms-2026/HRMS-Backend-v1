using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.CoreHr.People;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListChecklistAssigneePositions;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListChecklistAssignees;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

[ApiController]
[Route("api/v1/people")]
[Authorize(Policy = "TenantPolicy")]
public sealed class PeopleChecklistAssigneesController(IMediator mediator) : ControllerBase
{
    /// <summary>Active positions for the onboarding checklist assignee picker. Uses employees:write
    /// so Add Employee does not depend on org:read. Tenant is server-derived.</summary>
    [HttpGet("checklist-assignee-positions")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> ListPositions(
        [FromQuery] Guid legalEntityId,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListChecklistAssigneePositionsQuery(legalEntityId), ct);
        return result.IsSuccess
            ? Ok(result.Value!.Select(p => new ChecklistAssigneePositionViewModel(p.Id, p.Name)).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Active employees currently seated in a position, including the user id required
    /// to assign an onboarding checklist task. Onboarding-only picker; tenant is server-derived.</summary>
    [HttpGet("checklist-assignees")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> List(
        [FromQuery] Guid legalEntityId,
        [FromQuery] Guid positionId,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListChecklistAssigneesQuery(legalEntityId, positionId), ct);
        return result.IsSuccess
            ? Ok(result.Value!.Select(a => new ChecklistAssigneeViewModel(
                a.EmployeeId, a.UserId, a.DisplayName, a.WorkEmail, a.AvatarFileId)).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
