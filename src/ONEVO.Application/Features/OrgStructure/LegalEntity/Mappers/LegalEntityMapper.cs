using System.Text.Json;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Domain.Features.OrgStructure.Entities;

// Namespace deliberately stops at the feature segment - a ".LegalEntity"
// segment would collide with the LegalEntity entity type used throughout
// this file (same convention as ILegalEntityRepository / LegalEntityConfiguration).
namespace ONEVO.Application.Features.OrgStructure.Mappers;

public static class LegalEntityMapper
{
    public static LegalEntityListItemResponse ToListItemResponse(LegalEntity entity)
    {
        return new LegalEntityListItemResponse(
            entity.Id,
            entity.Name,
            entity.CompanyCode,
            entity.LogoFileId,
            entity.IsActive,
            entity.IsPrimary);
    }

    public static LegalEntityGeneralSettingsResponse ToGeneralSettingsResponse(LegalEntity entity)
    {
        return new LegalEntityGeneralSettingsResponse(
            entity.Id,
            entity.Name,
            entity.CompanyCode,
            entity.LogoFileId,
            entity.RegistrationNumber,
            entity.TaxRegistrationNumber,
            entity.VatGstNumber,
            entity.Email,
            entity.PhoneNumber,
            entity.Website,
            entity.CountryCode,
            entity.CurrencyCode,
            entity.Timezone,
            entity.FinancialYearStartMonth,
            entity.FirstDayOfWeek,
            ParseStandardWorkingDays(entity.StandardWorkingDays),
            entity.DefaultLanguage,
            entity.DateFormat,
            entity.TimeFormat,
            entity.IsActive ? "active" : "inactive");
    }

    public static IReadOnlyList<int> ParseStandardWorkingDays(string standardWorkingDaysJson)
    {
        return JsonSerializer.Deserialize<List<int>>(standardWorkingDaysJson) ?? [];
    }

    public static string SerializeStandardWorkingDays(IEnumerable<int> days)
    {
        return JsonSerializer.Serialize(days.Distinct().OrderBy(d => d).ToList());
    }
}
