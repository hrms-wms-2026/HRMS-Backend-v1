using System.Text.Json.Serialization;
using ONEVO.Application.Features.Auth.Invite.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Invite.DTOs.Responses;

public sealed record InvitationDetailDto(
    [property: JsonPropertyName("invitation_id")] Guid InvitationId,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("tenant_name")] string TenantName,
    [property: JsonPropertyName("invited_email")] string InvitedEmail,
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("role_name")] string RoleName,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("password_setup_enabled")] bool PasswordSetupEnabled,
    [property: JsonPropertyName("google_sign_in_enabled")] bool GoogleSignInEnabled,
    [property: JsonPropertyName("allow_google_email_mismatch")] bool AllowGoogleEmailMismatch,
    [property: JsonPropertyName("allowed_email_domains")] IReadOnlyList<string> AllowedEmailDomains,
    [property: JsonPropertyName("requires_password")] bool RequiresPassword);
