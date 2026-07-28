using System.Text.Json.Serialization;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Login.DTOs.Responses;

public record CurrentUserDto(
    [property: JsonIgnore] Guid UserId,
    [property: JsonIgnore] Guid TenantId,
    [property: JsonPropertyName("email")] string Email
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
    [property: JsonPropertyName("continue_url")] string? ContinueUrl = null
);
