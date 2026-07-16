namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;

public record PlatformUserDetailResponse(
    Guid Id,
    string Email,
    string? FirstName,
    string? LastName,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<PlatformRoleResponse> Roles);
