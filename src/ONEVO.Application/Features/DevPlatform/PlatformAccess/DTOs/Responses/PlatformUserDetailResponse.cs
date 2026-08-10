namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;

public record PlatformUserDetailResponse(
    Guid Id,
    string Email,
    string FullName,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<PlatformRoleResponse> Roles);
