using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
namespace ONEVO.Application.Features.Auth.Login.DTOs.Responses;

/// <summary>
/// Internal handler-to-controller result. CsrfToken/CsrfTokenHash are the raw/hashed CSRF values the
/// controller stashes on AuthenticationProperties.Items before calling HttpContext.SignInAsync — they
/// must never be serialized to the client. Only ToSessionResponse() is ever returned as JSON.
/// </summary>
public record LoginResponseDto(
    string CsrfToken,
    string CsrfTokenHash,
    DateTimeOffset? ExpiresAt,
    CurrentUserDto? User = null,
    IReadOnlyList<string>? Permissions = null,
    IReadOnlyList<string>? ActiveModules = null,
    bool RequiresPasswordChange = false,
    bool RequiresMfa = false,
    string? MfaChallenge = null
)
{
    public AuthSessionResponseDto ToSessionResponse() =>
        new(
            Authenticated: !RequiresPasswordChange && !RequiresMfa && User is not null,
            User: User,
            Permissions: Permissions ?? [],
            ActiveModules: ActiveModules ?? [],
            MustChangePassword: RequiresPasswordChange,
            MfaRequired: RequiresMfa,
            ExpiresAt: (!RequiresPasswordChange && !RequiresMfa) ? ExpiresAt : null);
}
