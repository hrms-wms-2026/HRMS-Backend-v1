namespace ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

public sealed record TenantAuditLogEntryResponse(
    Guid Id,
    Guid? UserId,
    string? UserEmail,
    string Action,
    string ResourceType,
    Guid? ResourceId,
    string? IpAddress,
    DateTimeOffset CreatedAt);

public sealed record TenantAuditLogListResponse(
    IReadOnlyList<TenantAuditLogEntryResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);
