using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
namespace ONEVO.Application.Features.Auth.Login.DTOs.Responses;

/// <summary>
/// Internal handler-to-controller result. CsrfToken/CsrfTokenHash are the raw/hashed CSRF values the
/// controller stashes on AuthenticationProperties.Items before calling HttpContext.SignInAsync;
/// LegalChallenge/LegalCsrfToken are the raw values the controller sets as the onevo_legal_pending
/// (HttpOnly) and onevo_legal_csrf (readable) cookies - none of the four are ever serialized to the
/// client. Only ToSessionResponse() is ever returned as JSON.
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
    string? MfaChallenge = null,
    bool RequiresLegalAcceptance = false,
    string? LegalChallenge = null,
    string? LegalCsrfToken = null,
    IReadOnlyList<PendingLegalDocumentDto>? PendingLegalDocuments = null
)
{
    public AuthSessionResponseDto ToSessionResponse() =>
        new(
            Authenticated: !RequiresPasswordChange && !RequiresMfa && !RequiresLegalAcceptance && User is not null,
            User: User,
            Permissions: Permissions ?? [],
            ActiveModules: ActiveModules ?? [],
            MustChangePassword: RequiresPasswordChange,
            MfaRequired: RequiresMfa,
            LegalAcceptanceRequired: RequiresLegalAcceptance,
            PendingLegalDocuments: RequiresLegalAcceptance ? (PendingLegalDocuments ?? []) : null,
            ExpiresAt: (!RequiresPasswordChange && !RequiresMfa && !RequiresLegalAcceptance) ? ExpiresAt : null);
}
