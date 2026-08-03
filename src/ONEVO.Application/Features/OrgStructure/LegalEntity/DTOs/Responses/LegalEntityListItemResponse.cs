namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record LegalEntityListItemResponse(
    Guid Id,
    string Name,
    string? CompanyCode,
    Guid? LogoFileId,
    bool IsActive,
    bool IsPrimary);
