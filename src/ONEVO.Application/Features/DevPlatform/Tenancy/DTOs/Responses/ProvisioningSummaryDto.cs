namespace ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

public sealed record ProvisioningSummaryDto(
    Guid TenantId,
    string Status,
    ProvisioningSectionsDto Sections,
    bool CanActivate,
    IReadOnlyList<ProvisioningIssueDto> BlockingErrors,
    IReadOnlyList<ProvisioningIssueDto> Warnings);

public sealed record ProvisioningSectionsDto(
    ProvisioningSectionStatusDto TenantDetails,
    ProvisioningSectionStatusDto Subscription,
    ProvisioningSectionStatusDto Modules,
    ProvisioningSectionStatusDto Roles,
    ProvisioningSectionStatusDto Settings,
    ProvisioningSectionStatusDto OwnerInvite);

public sealed record ProvisioningSectionStatusDto(
    bool Complete,
    IReadOnlyDictionary<string, object?> Summary,
    IReadOnlyList<string> MissingFields);

public sealed record ProvisioningIssueDto(string Code, string Message, string Section);
