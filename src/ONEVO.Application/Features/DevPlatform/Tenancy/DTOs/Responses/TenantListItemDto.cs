namespace ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

public sealed record TenantListItemDto(
    Guid Id,
    string Name,
    string Slug,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record TenantListResponseDto(
    IReadOnlyList<TenantListItemDto> Items,
    int Total,
    int Page,
    int PageSize);

public sealed record CreateTenantDraftResponseDto(
    Guid TenantId,
    string Status,
    string NextStep,
    OwnerInviteResultDto? OwnerInvite = null);

public sealed record OwnerInviteResultDto(
    Guid UserId,
    DateTimeOffset InviteExpiresAt,
    string DeliveryStatus);
