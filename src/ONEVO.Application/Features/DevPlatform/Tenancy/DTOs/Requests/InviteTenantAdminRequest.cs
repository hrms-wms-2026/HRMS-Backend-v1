using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;

public sealed record InviteTenantAdminRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("role_id")] Guid RoleId,
    [property: JsonPropertyName("completion_methods")] IReadOnlyList<string>? CompletionMethods,
    [property: JsonPropertyName("allow_google_email_mismatch")] bool? AllowGoogleEmailMismatch,
    [property: JsonPropertyName("allowed_email_domains")] IReadOnlyList<string>? AllowedEmailDomains);

public sealed record TenantOwnerInviteRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("completion_methods")] IReadOnlyList<string>? CompletionMethods,
    [property: JsonPropertyName("allow_google_email_mismatch")] bool? AllowGoogleEmailMismatch,
    [property: JsonPropertyName("allowed_email_domains")] IReadOnlyList<string>? AllowedEmailDomains);
