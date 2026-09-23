using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Requests;

public sealed record StartDeviceAuthorizationRequest(
    [property: JsonPropertyName("device_name")] string DeviceName,
    [property: JsonPropertyName("device_os")] string DeviceOs,
    [property: JsonPropertyName("device_fingerprint")] string DeviceFingerprint,
    [property: JsonPropertyName("client_version")] string ClientVersion);
