using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Commands.CreateLegalEntity;

// Deliberately has no TenantId parameter - the handler sources it from
// ICurrentUser.TenantId, never from caller input.
public record CreateLegalEntityCommand(
    string Name,
    string? CompanyCode,
    string RegistrationNumber,
    string CountryCode,
    string CurrencyCode,
    string? TaxRegistrationNumber,
    LegalEntityAddressDto? RegisteredBusinessAddress,
    Guid? ParentLegalEntityId) : IRequest<Result<LegalEntityGeneralSettingsResponse>>;
