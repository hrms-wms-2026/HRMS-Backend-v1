using System.Text.Json.Serialization;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Login.DTOs.Responses;

public record CurrentUserDto(
    [property: JsonIgnore] Guid UserId,
    [property: JsonIgnore] Guid TenantId,
    [property: JsonPropertyName("email")] string Email,
    Guid? EmployeeId = null
);

/// <summary>
/// Public workspace identity exposed to the browser once a tenant has been resolved. Only the slug
/// (used to build the tenant app URL, e.g. https://{slug}.onevo.com) and a human-readable display
/// name are ever serialized here - never the tenant's internal Guid id.
/// </summary>
public record WorkspaceResponseDto(
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("display_name")] string DisplayName
);

public record AuthSessionResponseDto(
    [property: JsonPropertyName("authenticated")] bool Authenticated,
    [property: JsonPropertyName("user")] CurrentUserDto? User,
    [property: JsonPropertyName("permissions")] IReadOnlyList<string> Permissions,
    [property: JsonPropertyName("active_modules")] IReadOnlyList<string> ActiveModules,
    [property: JsonPropertyName("must_change_password")] bool MustChangePassword,
    [property: JsonPropertyName("mfa_required")] bool MfaRequired,
    [property: JsonPropertyName("legal_acceptance_required")] bool LegalAcceptanceRequired = false,
    [property: JsonPropertyName("pending_legal_documents")] IReadOnlyList<PendingLegalDocumentDto>? PendingLegalDocuments = null,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt = null,
    [property: JsonPropertyName("continue_url")] string? ContinueUrl = null,
    [property: JsonPropertyName("workspace")] WorkspaceResponseDto? Workspace = null
);
