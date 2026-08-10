using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Commands.UpdateLegalEntityGeneralSettings;

// No TenantId parameter (sourced from ICurrentUser) and no
// ParentLegalEntityId - the General Settings screen does not expose parent
// company as an editable field per this task's spec (only Create Company does).
public record UpdateLegalEntityGeneralSettingsCommand(
    Guid LegalEntityId,
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
    string Status) : IRequest<Result<LegalEntityGeneralSettingsResponse>>;
