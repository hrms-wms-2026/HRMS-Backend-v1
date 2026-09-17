using System.Text.Json.Serialization;
using ONEVO.Application.Features.Auth.Legal.Services;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

public record TrayAuthResponseDto(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in_seconds")] int ExpiresInSeconds,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("refresh_expires_in_seconds")] int RefreshExpiresInSeconds,
    [property: JsonPropertyName("employee_name")] string? EmployeeName = null,
    [property: JsonPropertyName("employee_email")] string? EmployeeEmail = null,
    [property: JsonPropertyName("employee_number")] string? EmployeeNumber = null,
    [property: JsonPropertyName("employee_profile_status")] string EmployeeProfileStatus = "resolved",
    [property: JsonPropertyName("tenant_slug")] string? TenantSlug = null,
    [property: JsonPropertyName("department_name")] string? DepartmentName = null,
    [property: JsonPropertyName("work_mode_label")] string? WorkModeLabel = null,
    [property: JsonPropertyName("office_name")] string? OfficeName = null,
    [property: JsonPropertyName("organization_name")] string? OrganizationName = null,
    [property: JsonPropertyName("legal_acceptance_required")] bool RequiresLegalAcceptance = false,
    [property: JsonPropertyName("pending_legal_documents")] IReadOnlyList<PendingLegalDocumentDto>? PendingLegalDocuments = null,
    [property: JsonPropertyName("legal_challenge")] string? LegalChallenge = null,
    [property: JsonPropertyName("legal_csrf_token")] string? LegalCsrfToken = null);
