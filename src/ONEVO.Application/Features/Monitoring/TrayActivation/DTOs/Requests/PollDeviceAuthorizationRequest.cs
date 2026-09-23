using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Requests;

public sealed record PollDeviceAuthorizationRequest(
    [property: JsonPropertyName("device_code")] string DeviceCode,
    [property: JsonPropertyName("device_fingerprint")] string DeviceFingerprint);
