namespace ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

public sealed record TenantSessionResponse(
    Guid Id,
    Guid UserId,
    string? UserEmail,
    string? UserFullName,
    string? IpAddress,
    string? UserAgent,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActivityAt,
    DateTimeOffset ExpiresAt);
