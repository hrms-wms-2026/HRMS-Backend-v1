namespace ONEVO.Application.Common.ServiceInterfaces;

public record FaceLivenessSession(string SessionId, string Region);

public record FaceLivenessOutcome(string Status, float Confidence, MemoryStream? ReferenceImageBytes);

public record ScopedAwsCredentials(
    string AccessKeyId, string SecretAccessKey, string SessionToken, DateTimeOffset ExpiresAt);

public interface IFaceLivenessService
{
    Task<FaceLivenessSession> CreateSessionAsync(CancellationToken ct);

    Task<FaceLivenessOutcome> GetSessionResultAsync(string sessionId, CancellationToken ct);

    Task<ScopedAwsCredentials> AssumeLivenessRoleAsync(string sessionId, CancellationToken ct);
}
