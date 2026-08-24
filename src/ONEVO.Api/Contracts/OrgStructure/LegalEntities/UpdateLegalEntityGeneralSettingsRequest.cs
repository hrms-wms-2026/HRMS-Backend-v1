namespace ONEVO.Api.Contracts.OrgStructure.LegalEntities;

// Full-field PUT payload for the General Settings screen. Deliberately
// excludes tenantId (sourced from ICurrentUser) and parentLegalEntityId
// (not part of the General Settings edit-field set per this task's spec -
// only Create Company collects it).
public record UpdateLegalEntityGeneralSettingsRequest(
    string Name,
    string CompanyCode,
    string RegistrationNumber,
    string? TaxRegistrationNumber,
    string? VatGstNumber,
    string? Email,
    string? PhoneNumber,
    string? Website,
    string CountryCode,
    string CurrencyCode,
    string Timezone,
    int FinancialYearStartMonth,
    int FirstDayOfWeek,
    IReadOnlyList<int> StandardWorkingDays,
    string DefaultLanguage,
    string DateFormat,
    string TimeFormat,
    string Status,
    TimeOnly? WorkStartTime,
    TimeOnly? WorkEndTime,
    int? BreakDurationMinutes);
