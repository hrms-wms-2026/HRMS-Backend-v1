using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.CoreHr.BulkOnboarding;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.PreviewBulkOnboardingMapping;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.RequestBulkOnboardingDraftCreation;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.RequestBulkOnboardingFinalize;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.UploadBulkOnboardingBatch;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Queries.GetBulkOnboardingTemplate;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ValidateBulkOnboardingBatch;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Queries.GetBulkOnboardingBatch;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

[ApiController]
[Route("api/v1/onboarding/bulk-batches")]
[Authorize(Policy = "TenantPolicy")]
public class BulkOnboardingController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly GetBulkOnboardingTemplateQueryHandler _templateHandler;

    public BulkOnboardingController(IMediator mediator, GetBulkOnboardingTemplateQueryHandler templateHandler)
    {
        _mediator = mediator;
        _templateHandler = templateHandler;
    }

    [HttpPost]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> Upload([FromForm] UploadBulkOnboardingBatchRequest request, CancellationToken ct = default)
    {
        using var memoryStream = new MemoryStream();
        await request.File.CopyToAsync(memoryStream, ct);
        var fileContent = memoryStream.ToArray();

        var command = new UploadBulkOnboardingBatchCommand(
            request.File.FileName, fileContent, request.LegalEntityId,
            request.DefaultWorkModeId, request.DefaultEmploymentType, request.DefaultChecklistTemplateId);

        var result = await _mediator.Send(command, ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var response = result.Value!;
        return Ok(new BulkOnboardingBatchViewModel(
            response.Id, response.Status, response.TotalRows, response.ValidRows, response.InvalidRows,
            response.DetectedColumns, response.SuggestedMapping));
    }

    [HttpGet("template")]
    [RequirePermission("employees:write")]
    public IActionResult GetTemplate([FromQuery] string format)
    {
        var result = _templateHandler.Handle(new GetBulkOnboardingTemplateQuery(format));
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var file = result.Value!;
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost("{id:guid}/preview")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> Preview(Guid id, [FromBody] PreviewBulkOnboardingMappingRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new PreviewBulkOnboardingMappingCommand(id, request.Mapping), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var r = result.Value!;
        return Ok(new BulkOnboardingRowPreviewViewModel(
            r.FirstName, r.LastName, r.WorkEmail, r.StartDate, r.EmploymentType,
            r.WorkModeName, r.DepartmentName, r.PositionName, r.ChecklistTemplateName, r.EmployeeNumber));
    }

    [HttpPost("{id:guid}/validate")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> Validate(Guid id, [FromBody] ValidateBulkOnboardingBatchRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ValidateBulkOnboardingBatchCommand(id, request.Mapping), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var r = result.Value!;
        return Ok(new ValidateBulkOnboardingBatchResponse(
            r.ValidRows, r.InvalidRows,
            r.Rows.Select(row => new BulkOnboardingRowValidationItem(row.RowNumber, row.Status, row.ErrorMessage)).ToList()));
    }

    [HttpPost("{id:guid}/create-drafts")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> CreateDrafts(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RequestBulkOnboardingDraftCreationCommand(id), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var response = result.Value!;
        return Ok(new BulkOnboardingBatchViewModel(
            response.Id, response.Status, response.TotalRows, response.ValidRows, response.InvalidRows,
            response.DetectedColumns, response.SuggestedMapping));
    }

    [HttpPost("{id:guid}/finalize")]
    [RequirePermission("employees:write")]
    [Idempotent]
    public async Task<IActionResult> Finalize(Guid id, [FromBody] FinalizeBulkOnboardingBatchRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RequestBulkOnboardingFinalizeCommand(id, request.OnboardingDraftIds), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var response = result.Value!;
        return Ok(new BulkOnboardingBatchViewModel(
            response.Id, response.Status, response.TotalRows, response.ValidRows, response.InvalidRows,
            response.DetectedColumns, response.SuggestedMapping));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetBulkOnboardingBatchQuery(id), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var r = result.Value!;
        return Ok(new BulkOnboardingBatchDetailViewModel(
            r.Id, r.Status, r.TotalRows, r.ValidRows, r.InvalidRows,
            r.Rows.Select(row => new BulkOnboardingBatchRowDetailViewModel(
                row.RowNumber, row.Status, row.ErrorMessage, row.OnboardingDraftId)).ToList()));
    }
}
