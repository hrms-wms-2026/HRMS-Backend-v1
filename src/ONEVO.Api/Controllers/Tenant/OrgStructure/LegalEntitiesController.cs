using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.OrgStructure.LegalEntities;
using ONEVO.Api.Contracts.Storage;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.OrgStructure.Commands.CreateLegalEntity;
using ONEVO.Application.Features.OrgStructure.Commands.DeleteLegalEntity;
using ONEVO.Application.Features.OrgStructure.Commands.LinkLegalEntityLogo;
using ONEVO.Application.Features.OrgStructure.Commands.RemoveLegalEntityLogoAsset;
using ONEVO.Application.Features.OrgStructure.Commands.UpdateLegalEntityGeneralSettings;
using ONEVO.Application.Features.OrgStructure.Queries.GetLegalEntityGeneralSettings;
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
                request.BreakDurationMinutes,
                request.OfficeAddress,
                request.OfficeLatitude,
                request.OfficeLongitude),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Links a pending company_logo upload as this company's current logo.</summary>
    [HttpPut("{id:guid}/logo")]
    [RequirePermission("legal_entity:update")]
    public async Task<IActionResult> LinkLogo(Guid id, [FromBody] LinkFileRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new LinkLegalEntityLogoCommand(id, request.FileId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Unlinks and deletes this company's current logo.</summary>
    [HttpDelete("{id:guid}/logo")]
    [RequirePermission("legal_entity:update")]
    public async Task<IActionResult> RemoveLogo(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new RemoveLegalEntityLogoAssetCommand(id), ct);
        return result.IsSuccess
            ? NoContent()
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

}
