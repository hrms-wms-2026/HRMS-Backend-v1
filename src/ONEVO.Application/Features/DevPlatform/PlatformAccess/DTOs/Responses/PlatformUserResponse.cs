namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;

public record PlatformUserResponse(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);
