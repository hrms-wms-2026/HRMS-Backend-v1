using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

public record ActivationCodeResponseDto(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("expires_in_seconds")] int ExpiresInSeconds);
