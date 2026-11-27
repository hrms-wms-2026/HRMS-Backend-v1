namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;

public record PlatformUserResponse(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    // "pending" | "accepted" | "revoked" | "expired". Lets the console tell a revoked
    // invite (can be re-invited) apart from a deactivated account (both are inactive).
    string InviteStatus);
