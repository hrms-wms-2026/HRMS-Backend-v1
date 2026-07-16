namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;

public record PlatformRoleDetailResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystemRole,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Permissions);
