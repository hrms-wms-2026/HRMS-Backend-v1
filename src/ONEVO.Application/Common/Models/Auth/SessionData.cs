namespace ONEVO.Application.Common.Models.Auth;

public record SessionData(
    Guid UserId,
    Guid TenantId,
    string Email,
    string[] Permissions,
    DateTimeOffset ExpiresAt,
    string CsrfTokenHash);
