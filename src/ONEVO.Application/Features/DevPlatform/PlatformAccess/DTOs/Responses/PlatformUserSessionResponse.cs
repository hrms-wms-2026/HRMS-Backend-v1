namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;

public record PlatformUserSessionResponse(
    Guid Id,
    Guid UserId,
    string? DeviceInfo,
    string? IpAddress,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    bool IsRevoked);
