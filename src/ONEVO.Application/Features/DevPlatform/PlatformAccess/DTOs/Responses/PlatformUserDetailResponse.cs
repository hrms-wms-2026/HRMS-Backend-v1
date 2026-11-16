namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;

public record PlatformUserDetailResponse(
    Guid Id,
    string Email,
    string FullName,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<PlatformRoleResponse> Roles,
    // F-02: "enrolled" / "not_enrolled" / "locked", so the console can offer an MFA reset (D2).
    // The TOTP secret itself is never part of any response.
    string MfaStatus);
