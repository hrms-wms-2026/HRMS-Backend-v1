using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Commands.UpdateLegalEntityGeneralSettings;

public class UpdateLegalEntityGeneralSettingsCommandHandler
    : IRequestHandler<UpdateLegalEntityGeneralSettingsCommand, Result<LegalEntityGeneralSettingsResponse>>
{
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateLegalEntityGeneralSettingsCommandHandler(
        ILegalEntityRepository legalEntities, ICurrentUser currentUser, IDateTimeProvider dateTimeProvider)
    {
        _legalEntities = legalEntities;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<LegalEntityGeneralSettingsResponse>> Handle(
        UpdateLegalEntityGeneralSettingsCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LegalEntityGeneralSettingsResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<LegalEntityGeneralSettingsResponse>.Forbidden("Tenant context missing.");

        // Fetch-then-mutate: never construct a detached LegalEntity and call
        // Update() directly - GetByIdForTenantAsync loads the tracked-free
        // instance we mutate below, exactly as Part 2A's repository doc
        // mandates.
        var entity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (entity is null)
            return Result<LegalEntityGeneralSettingsResponse>.NotFound("Company not found.");

        var name = request.Name.Trim();
        var companyCode = request.CompanyCode.Trim();
        var registrationNumber = request.RegistrationNumber.Trim();

        if (await _legalEntities.NameExistsForTenantAsync(tenantId, name, entity.Id, ct))
            return Result<LegalEntityGeneralSettingsResponse>.Conflict("Company name already exists.");

        if (await _legalEntities.CompanyCodeExistsForTenantAsync(tenantId, companyCode, entity.Id, ct))
            return Result<LegalEntityGeneralSettingsResponse>.Conflict("A company with this code already exists.");

        if (await _legalEntities.RegistrationNumberExistsForTenantAsync(tenantId, registrationNumber, entity.Id, ct))
            return Result<LegalEntityGeneralSettingsResponse>.Conflict(
                "A company with this registration number already exists.");

        var newIsActive = string.Equals(request.Status, "active", StringComparison.OrdinalIgnoreCase);
        if (entity.IsActive && !newIsActive)
        {
            var activeCount = await _legalEntities.CountActiveByTenantAsync(tenantId, ct);
            if (activeCount <= 1)
                return Result<LegalEntityGeneralSettingsResponse>.Failure(
                    "Cannot delete or deactivate the last active company in the tenant.");
        }

        entity.Name = name;
        entity.CompanyCode = companyCode;
        entity.RegistrationNumber = registrationNumber;
        entity.TaxRegistrationNumber = request.TaxRegistrationNumber?.Trim();
        entity.VatGstNumber = request.VatGstNumber?.Trim();
        entity.Email = request.Email?.Trim();
        entity.PhoneNumber = request.PhoneNumber?.Trim();
        entity.Website = request.Website?.Trim();
        entity.CountryCode = request.CountryCode.Trim();
        entity.CurrencyCode = request.CurrencyCode.Trim();
        entity.Timezone = request.Timezone.Trim();
        entity.FinancialYearStartMonth = request.FinancialYearStartMonth;
        entity.FirstDayOfWeek = request.FirstDayOfWeek;
        entity.StandardWorkingDays = LegalEntityMapper.SerializeStandardWorkingDays(request.StandardWorkingDays);
        entity.DefaultLanguage = request.DefaultLanguage.Trim();
        entity.DateFormat = request.DateFormat.Trim();
        entity.TimeFormat = request.TimeFormat.Trim();
        entity.IsActive = newIsActive;
        entity.AddressJson = LegalEntityMapper.SerializeAddress(request.RegisteredBusinessAddress);
        entity.UpdatedAt = _dateTimeProvider.UtcNow;

        // LogoFileId, ParentLegalEntityId, IsPrimary, CreatedAt, Id and
        // TenantId are intentionally left untouched above - they are not
        // part of this request's field set and must be preserved.
        _legalEntities.Update(entity);
        await _legalEntities.SaveChangesAsync(ct);

        return Result<LegalEntityGeneralSettingsResponse>.Success(LegalEntityMapper.ToGeneralSettingsResponse(entity));
    }
}
