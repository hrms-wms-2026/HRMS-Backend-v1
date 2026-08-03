using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.Commands.CreateLegalEntity;

public class CreateLegalEntityCommandHandler
    : IRequestHandler<CreateLegalEntityCommand, Result<LegalEntityGeneralSettingsResponse>>
{
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public CreateLegalEntityCommandHandler(ILegalEntityRepository legalEntities, ICurrentUser currentUser)
    {
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<LegalEntityGeneralSettingsResponse>> Handle(
        CreateLegalEntityCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LegalEntityGeneralSettingsResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<LegalEntityGeneralSettingsResponse>.Forbidden("Tenant context missing.");

        var name = request.Name.Trim();
        var companyCode = request.CompanyCode?.Trim();
        var registrationNumber = request.RegistrationNumber.Trim();

        if (await _legalEntities.NameExistsForTenantAsync(tenantId, name, null, ct))
            return Result<LegalEntityGeneralSettingsResponse>.Conflict("Company name already exists.");

        if (!string.IsNullOrEmpty(companyCode)
            && await _legalEntities.CompanyCodeExistsForTenantAsync(tenantId, companyCode, null, ct))
            return Result<LegalEntityGeneralSettingsResponse>.Conflict("A company with this code already exists.");

        if (await _legalEntities.RegistrationNumberExistsForTenantAsync(tenantId, registrationNumber, null, ct))
            return Result<LegalEntityGeneralSettingsResponse>.Conflict(
                "A company with this registration number already exists.");

        if (request.ParentLegalEntityId is { } parentId)
        {
            var parentExists = await _legalEntities.ParentExistsForTenantAsync(tenantId, parentId, ct);
            if (!parentExists)
                return Result<LegalEntityGeneralSettingsResponse>.NotFound("Parent company not found.");
        }

        var entity = new LegalEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            CompanyCode = companyCode,
            RegistrationNumber = registrationNumber,
            CountryCode = request.CountryCode.Trim(),
            CurrencyCode = request.CurrencyCode.Trim(),
            TaxRegistrationNumber = request.TaxRegistrationNumber?.Trim(),
            AddressJson = LegalEntityMapper.SerializeAddress(request.RegisteredBusinessAddress),
            ParentLegalEntityId = request.ParentLegalEntityId,

            // A company created through this org-structure flow is never the
            // tenant's primary company - IsPrimary is only ever set true by
            // CreateTenantCommandHandler during tenant provisioning, so it
            // must be forced false here despite the entity's own default.
            IsPrimary = false,
            IsActive = true,

            // Safe defaults per task instruction: no country-default helper
            // exists yet (Part 1 audit - no countries table), so timezone is
            // a fixed UTC default rather than guessed.
            Timezone = "UTC",
            FinancialYearStartMonth = 1,
            FirstDayOfWeek = 1,
            StandardWorkingDays = LegalEntityMapper.SerializeStandardWorkingDays([1, 2, 3, 4, 5]),
            DefaultLanguage = "en-US",
            DateFormat = "DD MMM YYYY",
            TimeFormat = "12h"
        };

        await _legalEntities.AddAsync(entity, ct);
        await _legalEntities.SaveChangesAsync(ct);

        return Result<LegalEntityGeneralSettingsResponse>.Success(LegalEntityMapper.ToGeneralSettingsResponse(entity));
    }
}
