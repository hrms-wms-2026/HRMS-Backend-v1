namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;

public record PlatformRoleResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystemRole,
    DateTimeOffset CreatedAt);
