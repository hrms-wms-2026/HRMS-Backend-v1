using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

public sealed record DeviceAuthorizationPreviewDto(
    [property: JsonPropertyName("request_id")] Guid RequestId,
    [property: JsonPropertyName("device_name")] string DeviceName,
    [property: JsonPropertyName("device_os")] string DeviceOs,
    [property: JsonPropertyName("client_version")] string ClientVersion,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("status")] string Status);
