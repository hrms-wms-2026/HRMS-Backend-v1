using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

public record TrayAuthResponseDto(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in_seconds")] int ExpiresInSeconds,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("refresh_expires_in_seconds")] int RefreshExpiresInSeconds,
    [property: JsonPropertyName("employee_name")] string? EmployeeName = null,
    [property: JsonPropertyName("employee_email")] string? EmployeeEmail = null,
    [property: JsonPropertyName("employee_number")] string? EmployeeNumber = null);
