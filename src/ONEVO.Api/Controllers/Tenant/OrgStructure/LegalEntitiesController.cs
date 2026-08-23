using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.OrgStructure.LegalEntities;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.OrgStructure.Commands.CreateLegalEntity;
using ONEVO.Application.Features.OrgStructure.Commands.DeleteLegalEntity;
using ONEVO.Application.Features.OrgStructure.Commands.RemoveLegalEntityLogo;
using ONEVO.Application.Features.OrgStructure.Commands.SetLegalEntityLogo;
using ONEVO.Application.Features.OrgStructure.Commands.UpdateLegalEntityGeneralSettings;
using ONEVO.Application.Features.OrgStructure.Queries.GetLegalEntityGeneralSettings;
using ONEVO.Application.Features.OrgStructure.Queries.GetLegalEntityLogo;
using ONEVO.Application.Features.OrgStructure.Queries.ListLegalEntities;

namespace ONEVO.Api.Controllers.Tenant.OrgStructure;

[ApiController]
[Route("api/v1/org/legal-entities")]
[Authorize(Policy = "TenantPolicy")]
public class LegalEntitiesController : ControllerBase
{
    private readonly IMediator _mediator;

    public LegalEntitiesController(IMediator mediator) => _mediator = mediator;

    /// <summary>Company selector/list for the current tenant. Active companies only unless includeInactive=true.</summary>
    [HttpGet]
    [RequirePermission("org:read")]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListLegalEntitiesQuery(includeInactive), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>General Settings for one company. 404 if missing or belongs to another tenant.</summary>
    [HttpGet("{id:guid}/general-settings")]
    [RequirePermission("legal_entity:update")]
    public async Task<IActionResult> GetGeneralSettings(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetLegalEntityGeneralSettingsQuery(id), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Create a company/legal entity inside the current tenant.</summary>
    [HttpPost]
    [RequirePermission("legal_entity:create")]
    public async Task<IActionResult> Create([FromBody] CreateLegalEntityRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateLegalEntityCommand(
                request.Name,
                request.CompanyCode,
                request.RegistrationNumber,
                request.CountryCode,
                request.CurrencyCode,
                request.TaxRegistrationNumber,
                request.ParentLegalEntityId),
            ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetGeneralSettings), new { id = result.Value!.Id }, result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Update General Settings for one company. The route id is authoritative.</summary>
    [HttpPut("{id:guid}/general-settings")]
    [RequirePermission("legal_entity:update")]
    public async Task<IActionResult> UpdateGeneralSettings(
        Guid id,
        [FromBody] UpdateLegalEntityGeneralSettingsRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new UpdateLegalEntityGeneralSettingsCommand(
                id,
                request.Name,
                request.CompanyCode,
                request.RegistrationNumber,
                request.TaxRegistrationNumber,
                request.VatGstNumber,
                request.Email,
                request.PhoneNumber,
                request.Website,
                request.CountryCode,
                request.CurrencyCode,
                request.Timezone,
                request.FinancialYearStartMonth,
                request.FirstDayOfWeek,
                request.StandardWorkingDays,
                request.DefaultLanguage,
                request.DateFormat,
                request.TimeFormat,
                request.Status,
                request.WorkStartTime,
                request.WorkEndTime,
                request.BreakDurationMinutes),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Soft-deactivates a company. Requires exact confirmName match; never physically deletes the row.</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("legal_entity:delete")]
    public async Task<IActionResult> Delete(Guid id, [FromBody] DeleteLegalEntityRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteLegalEntityCommand(id, request.ConfirmName), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Clears the company's logo reference. Never touches the underlying file_records row.</summary>
    [HttpDelete("{id:guid}/logo")]
    [RequirePermission("legal_entity:update")]
    public async Task<IActionResult> RemoveLogo(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new RemoveLegalEntityLogoCommand(id), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Streams the company's logo image. 404 if no logo is set.</summary>
    [HttpGet("{id:guid}/logo")]
    [RequirePermission("legal_entity:update")]
    public async Task<IActionResult> GetLogo(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetLegalEntityLogoQuery(id), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return File(result.Value!.Content, result.Value!.ContentType);
    }

    /// <summary>Uploads/replaces the company's logo. Accepts multipart/form-data with a "logo" file field.</summary>
    [HttpPut("{id:guid}/logo")]
    [RequestSizeLimit(6 * 1024 * 1024)] // 6 MB limit (5 MB image + overhead)
    [RequirePermission("legal_entity:update")]
    public async Task<IActionResult> SetLogo(Guid id, IFormFile logo, CancellationToken ct)
    {
        if (logo is null || logo.Length == 0)
            return Problem("logo file is required.", statusCode: 400);

        await using var stream = logo.OpenReadStream();
        var result = await _mediator.Send(
            new SetLegalEntityLogoCommand(id, stream, logo.ContentType, logo.FileName), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
