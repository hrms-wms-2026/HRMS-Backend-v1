namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

// Reused verbatim as the Create Company response (task explicitly permits
// reusing this DTO for CreateLegalEntityResponse rather than defining a
// second, near-identical type).
public record LegalEntityGeneralSettingsResponse(
    Guid Id,
    string Name,
    string? CompanyCode,
    Guid? LogoFileId,
    string? RegistrationNumber,
    string? TaxRegistrationNumber,
    string? VatGstNumber,
    string? Email,
    string? PhoneNumber,
    string? Website,
    string CountryCode,
    string CurrencyCode,
    string? Timezone,
    int FinancialYearStartMonth,
    int FirstDayOfWeek,
    IReadOnlyList<int> StandardWorkingDays,
    string DefaultLanguage,
    string DateFormat,
    string TimeFormat,
    string Status);
