using ONEVO.Application.Features.DevPlatform.Provisioning;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Mappers;

internal static class TenancyMapper
{
    public static TenantListItemDto ToListItemDto(Tenant tenant) =>
        new(tenant.Id, tenant.Name, tenant.Slug,
            tenant.Status.ToString().ToLowerInvariant(), tenant.CreatedAt);

    public static TenantListResponseDto ToListResponseDto(
        IReadOnlyList<TenantListItemDto> items, int total, int page, int pageSize) =>
        new(items, total, page, pageSize);

    public static ProvisioningSectionStatusDto ToSectionStatusDto(ProvisioningSectionStatus status) =>
        new(status.Complete, status.Summary, status.MissingFields);

    public static ProvisioningIssueDto ToIssueDto(ProvisioningIssue issue) =>
        new(issue.Code, issue.Message, issue.Section);

    public static TenantValidationConflictDto ToConflictDto(string field, string message) =>
        new(field, message);

    public static TenantValidationWarningDto ToWarningDto(string field, string message) =>
        new(field, message);
}
