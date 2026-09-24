namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;

public record PlatformAuthEventResponse(
    Guid Id,
    Guid? UserId,
    string? UserEmail,
    string? UserFullName,
    string EventType,
    string? SourceIp,
    string? UserAgent,
    DateTimeOffset CreatedAt);
