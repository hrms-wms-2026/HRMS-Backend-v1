namespace ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;

public record EnrollmentAttemptResponse(
    Guid AttemptId,
    string AwsSessionId,
    string Region,
    string ChallengeType,
    string AccessKeyId,
    string SecretAccessKey,
    string SessionToken,
    DateTimeOffset CredentialsExpireAt);
