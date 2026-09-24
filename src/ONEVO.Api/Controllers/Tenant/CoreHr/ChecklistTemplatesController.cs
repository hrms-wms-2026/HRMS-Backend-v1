using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.CoreHr.ChecklistTemplates;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.ArchiveChecklistTemplate;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.CreateChecklistTemplate;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.UpdateChecklistTemplate;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.GetChecklistTemplate;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListChecklistTemplates;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

[ApiController]
[Route("api/v1/people/checklist-templates")]
[Authorize(Policy = "TenantPolicy")]
public class ChecklistTemplatesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ChecklistTemplatesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>List checklist templates for a company, optionally filtered by templateType,
    /// department, position, and inactive-inclusion.</summary>
    [HttpGet]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> List(
        [FromQuery] string? templateType, [FromQuery] Guid legalEntityId, [FromQuery] Guid? departmentId, [FromQuery] Guid? positionId,
        [FromQuery] bool includeInactive = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ListChecklistTemplatesQuery(templateType, legalEntityId, departmentId, positionId, includeInactive, page, pageSize), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetChecklistTemplateByIdQuery(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>TenantId is always server-derived; the request body carries no tenant/company
    /// override beyond the explicit legalEntityId scope field.</summary>
    [HttpPost]
    [RequirePermission("employees:write")]
    [Idempotent]
    public async Task<IActionResult> Create([FromBody] CreateChecklistTemplateRequest request, CancellationToken ct = default)
    {
        var command = new CreateChecklistTemplateCommand(
            request.Name, request.TemplateType, request.LegalEntityId, request.DepartmentId, request.PositionId,
            request.Tasks.Select(t => new CreateChecklistTemplateTaskInput(
                t.Title, t.OwnerType, t.AssignedToId, t.AssigneePositionId, t.DueOffsetDays, t.Sequence, t.IsRequired)).ToList());

        var result = await _mediator.Send(command, ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateChecklistTemplateRequest request, CancellationToken ct = default)
    {
        var command = new UpdateChecklistTemplateCommand(
            id, request.Name, request.DepartmentId, request.PositionId,
            request.Tasks.Select(t => new UpdateChecklistTemplateTaskInput(
                t.Title, t.OwnerType, t.AssignedToId, t.AssigneePositionId, t.DueOffsetDays, t.Sequence, t.IsRequired)).ToList());

        var result = await _mediator.Send(command, ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Soft-deactivate only - there is no delete endpoint for checklist templates.</summary>
    [HttpPost("{id:guid}/archive")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ArchiveChecklistTemplateCommand(id), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
