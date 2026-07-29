using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
namespace ONEVO.Application.Features.Auth.Login.DTOs.Responses;

/// <summary>
/// Continue-URL and expiry for a fully-authenticated base-domain login. Internal only - never
/// serialized directly; ToTenantSessionExchangeResponse() reads it to build the browser-facing
/// TenantSessionExchangeResponseDto.
/// </summary>
public sealed record TenantSessionExchangeMaterial(string ContinueUrl, DateTimeOffset ExpiresAt);

/// <summary>
/// Internal handler-to-controller result. CsrfToken/CsrfTokenHash are the raw/hashed CSRF values the
/// controller stashes on AuthenticationProperties.Items before calling HttpContext.SignInAsync;
/// LegalChallenge/LegalCsrfToken are the raw values the controller sets as the onevo_legal_pending
/// (HttpOnly) and onevo_legal_csrf (readable) cookies - none of the four are ever serialized to the
/// client. Only ToSessionResponse()/ToTenantSessionExchangeResponse() are ever returned as JSON.
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
    IReadOnlyList<PendingLegalDocumentDto>? PendingLegalDocuments = null,
    WorkspaceResponseDto? Workspace = null,
    TenantSessionExchangeMaterial? TenantSessionExchange = null
)
{
    /// <summary>
    /// True only for a base-domain login that cleared every gate (MFA/legal/password-change) and
    /// must hand off to the tenant host instead of signing in here. Mutually exclusive with the
    /// other Requires* flags by construction - see TenantSessionExchangeService.CreateAsync and
    /// LoginContinuationService.FinishAuthenticatedLoginAsync.
    /// </summary>
    public bool RequiresTenantSessionExchange => TenantSessionExchange is not null;

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
            ExpiresAt: (!RequiresPasswordChange && !RequiresMfa && !RequiresLegalAcceptance) ? ExpiresAt : null,
            Workspace: Workspace);

    public TenantSessionExchangeResponseDto ToTenantSessionExchangeResponse() =>
        new(
            Authenticated: false,
            RedirectRequired: true,
            User: new TenantSessionExchangeUserDto(User!.Email),
            Workspace: Workspace!,
            ContinueUrl: TenantSessionExchange!.ContinueUrl,
            ExpiresAt: TenantSessionExchange!.ExpiresAt);
}
