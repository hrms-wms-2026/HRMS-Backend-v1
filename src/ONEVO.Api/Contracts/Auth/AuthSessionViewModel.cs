using System.Text.Json.Serialization;

namespace ONEVO.Api.Contracts.Auth;

public record CurrentUserViewModel(
    [property: JsonPropertyName("email")] string Email
);

public record WorkspaceViewModel(
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("display_name")] string DisplayName
);

public record PendingLegalDocumentViewModel(
    [property: JsonPropertyName("document_type")] string DocumentType,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("effective_at")] DateTimeOffset? EffectiveAt,
    [property: JsonPropertyName("content_url")] string? ContentUrl,
    [property: JsonPropertyName("content_endpoint")] string ContentEndpoint,
    [property: JsonPropertyName("content_hash")] string ContentHash
);

public record AuthSessionViewModel(
    [property: JsonPropertyName("authenticated")] bool Authenticated,
    [property: JsonPropertyName("user")] CurrentUserViewModel? User,
    [property: JsonPropertyName("permissions")] IReadOnlyList<string> Permissions,
    [property: JsonPropertyName("active_modules")] IReadOnlyList<string> ActiveModules,
    [property: JsonPropertyName("must_change_password")] bool MustChangePassword,
    [property: JsonPropertyName("mfa_required")] bool MfaRequired,
    [property: JsonPropertyName("legal_acceptance_required")] bool LegalAcceptanceRequired = false,
    [property: JsonPropertyName("pending_legal_documents")] IReadOnlyList<PendingLegalDocumentViewModel>? PendingLegalDocuments = null,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt = null,
    [property: JsonPropertyName("continue_url")] string? ContinueUrl = null,
    [property: JsonPropertyName("workspace")] WorkspaceViewModel? Workspace = null
);
