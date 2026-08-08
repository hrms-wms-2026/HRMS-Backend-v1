namespace ONEVO.Api.Contracts.OrgStructure.LegalEntities;

// Deliberately excludes tenantId, logoFileId, isPrimary, createdAt, updatedAt,
// and any other system/internal field - tenant comes from ICurrentUser, the
// rest are set by the handler's safe defaults or a dedicated logo endpoint.
public record CreateLegalEntityRequest(
    string Name,
    string? CompanyCode,
    string RegistrationNumber,
    string CountryCode,
    string CurrencyCode,
    string? TaxRegistrationNumber,
    Guid? ParentLegalEntityId);
