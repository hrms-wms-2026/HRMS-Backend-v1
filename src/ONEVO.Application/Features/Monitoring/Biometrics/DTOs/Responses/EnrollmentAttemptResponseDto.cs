using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;

public record EnrollmentAttemptResponseDto(
    [property: JsonPropertyName("attempt_id")] Guid AttemptId,
    [property: JsonPropertyName("aws_session_id")] string AwsSessionId,
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("challenge_type")] string ChallengeType,
    [property: JsonPropertyName("access_key_id")] string AccessKeyId,
    [property: JsonPropertyName("secret_access_key")] string SecretAccessKey,
    [property: JsonPropertyName("session_token")] string SessionToken,
    [property: JsonPropertyName("credentials_expire_at")] DateTimeOffset CredentialsExpireAt);
