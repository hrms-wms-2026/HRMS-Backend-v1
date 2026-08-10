using System.Text.Json.Serialization;

namespace ONEVO.Api.Contracts.Auth;

public record TenantSessionExchangeUserViewModel(
    [property: JsonPropertyName("email")] string Email
);

public record TenantSessionExchangeViewModel(
    [property: JsonPropertyName("authenticated")] bool Authenticated,
    [property: JsonPropertyName("redirect_required")] bool RedirectRequired,
    [property: JsonPropertyName("user")] TenantSessionExchangeUserViewModel User,
    [property: JsonPropertyName("workspace")] WorkspaceViewModel Workspace,
    [property: JsonPropertyName("continue_url")] string ContinueUrl,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt
);
