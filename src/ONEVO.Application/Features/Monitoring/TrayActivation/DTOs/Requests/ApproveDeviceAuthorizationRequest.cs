using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Requests;

public sealed record ApproveDeviceAuthorizationRequest(
    [property: JsonPropertyName("request_id")] Guid RequestId,
    [property: JsonPropertyName("user_code")] string UserCode);
