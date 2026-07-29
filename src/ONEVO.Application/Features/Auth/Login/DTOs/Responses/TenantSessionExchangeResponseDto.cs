using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Auth.Login.DTOs.Responses;

/// <summary>
/// Returned to the base-domain browser once every login gate (credentials/Google, MFA, legal
/// acceptance, password change) has cleared. The base host never signs in and never sets
/// onevo_session/onevo_csrf here; ContinueUrl carries the one-time opaque exchange code as a query
/// parameter for the SPA to navigate to. Only the tenant host's POST /auth/session-exchange
/// consumes that code and creates the real session. No tenant_id, user_id, permissions, or token
/// material is ever included.
/// </summary>
public record TenantSessionExchangeResponseDto(
    [property: JsonPropertyName("authenticated")] bool Authenticated,
    [property: JsonPropertyName("redirect_required")] bool RedirectRequired,
    [property: JsonPropertyName("user")] TenantSessionExchangeUserDto User,
    [property: JsonPropertyName("workspace")] WorkspaceResponseDto Workspace,
    [property: JsonPropertyName("continue_url")] string ContinueUrl,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt
);

/// <summary>Deliberately narrower than CurrentUserDto: email only, no user id.</summary>
public record TenantSessionExchangeUserDto(
    [property: JsonPropertyName("email")] string Email
);
