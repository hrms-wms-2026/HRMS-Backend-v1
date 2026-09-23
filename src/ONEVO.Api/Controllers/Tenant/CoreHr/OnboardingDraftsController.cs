using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.CoreHr.OnboardingDrafts;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.FinalizeOnboardingDraft;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Queries.GetOnboardingDraft;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Queries.ListOnboardingDrafts;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

[ApiController]
[Route("api/v1/onboarding/drafts")]
[Authorize(Policy = "TenantPolicy")]
public class OnboardingDraftsController : ControllerBase
{
    private readonly IMediator _mediator;

    public OnboardingDraftsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>List onboarding drafts. Defaults to the caller's own drafts unless they hold
    /// employees:write.</summary>
    [HttpGet]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListOnboardingDraftsQuery(page, pageSize), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Get a single onboarding draft. 404 if it does not exist in the caller's
    /// tenant, 403 if it exists but the caller neither started it nor holds
    /// employees:write.</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetOnboardingDraftQuery(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Save Draft never sends an invite, never creates a user account, never
    /// activates checklist tasks, and never assigns policies - the draft reason
    /// (saved_manually / waiting_for_seat / waiting_for_position_approval) is resolved by the
    /// handler, not the caller. Idempotency-Key is honored so a duplicate retry does not
    /// create a second draft row.</summary>
    [HttpPost]
    [RequirePermission("employees:write")]
    [Idempotent]
    public async Task<IActionResult> Create([FromBody] SaveOnboardingDraftRequest request, CancellationToken ct = default)
    {
        var command = new SaveOnboardingDraftCommand(
            null, request.FirstName, request.LastName, request.WorkEmail, request.LegalEntityId, request.DepartmentId,
            request.PositionId, request.EmploymentType, request.StartDate, request.EmployeeNumber,
            request.WorkModeId, request.SelectedTemplateId, request.EditedTasksJson, request.LastSavedStep,
            IfMatchVersion: null, request.ReportsToEmployeeId);

        var result = await _mediator.Send(command, ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Update/resume an existing draft. If-Match, when present, is checked against
    /// the draft's current version; a stale write returns 409.</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveOnboardingDraftRequest request, CancellationToken ct = default)
    {
        var ifMatch = Request.Headers.IfMatch.FirstOrDefault()?.Trim('"');

        var command = new SaveOnboardingDraftCommand(
            id, request.FirstName, request.LastName, request.WorkEmail, request.LegalEntityId, request.DepartmentId,
            request.PositionId, request.EmploymentType, request.StartDate, request.EmployeeNumber,
            request.WorkModeId, request.SelectedTemplateId, request.EditedTasksJson, request.LastSavedStep,
            ifMatch, request.ReportsToEmployeeId);

        var result = await _mediator.Send(command, ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Converts a valid draft into a pending employee onboarding package: creates the
    /// pending user, employee, position assignment, checklist tasks, and invitation (queued via
    /// outbox) in one transaction, or - when the selected position requires approval - submits
    /// an access grant request and leaves the draft waiting for that approval. TenantId is
    /// always server-derived; the request carries no body.</summary>
    [HttpPost("{id:guid}/finalize")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> Finalize(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new FinalizeOnboardingDraftCommand(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
