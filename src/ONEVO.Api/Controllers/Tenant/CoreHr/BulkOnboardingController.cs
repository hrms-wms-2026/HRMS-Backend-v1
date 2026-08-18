using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.CoreHr.BulkOnboarding;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.PreviewBulkOnboardingMapping;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.UploadBulkOnboardingBatch;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ValidateBulkOnboardingBatch;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

[ApiController]
[Route("api/v1/onboarding/bulk-batches")]
[Authorize(Policy = "TenantPolicy")]
public class BulkOnboardingController : ControllerBase
{
    private readonly IMediator _mediator;
    public BulkOnboardingController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> Upload([FromForm] UploadBulkOnboardingBatchRequest request, CancellationToken ct = default)
    {
        using var reader = new StreamReader(request.File.OpenReadStream());
        var csvContent = await reader.ReadToEndAsync(ct);

        var command = new UploadBulkOnboardingBatchCommand(
            request.File.FileName, csvContent, request.LegalEntityId,
            request.DefaultWorkModeId, request.DefaultEmploymentType, request.DefaultChecklistTemplateId);

        var result = await _mediator.Send(command, ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var response = result.Value!;
        return Ok(new BulkOnboardingBatchViewModel(
            response.Id, response.Status, response.TotalRows, response.ValidRows, response.InvalidRows,
            response.DetectedColumns, response.SuggestedMapping));
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
}
