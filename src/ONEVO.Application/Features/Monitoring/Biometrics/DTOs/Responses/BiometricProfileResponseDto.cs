using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;

public record BiometricProfileResponseDto(
    [property: JsonPropertyName("profile_id")] Guid ProfileId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("enrolled_at")] DateTimeOffset EnrolledAt);
