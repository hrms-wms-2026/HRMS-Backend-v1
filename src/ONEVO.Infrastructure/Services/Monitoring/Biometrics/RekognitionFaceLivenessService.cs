using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Configuration;

namespace ONEVO.Infrastructure.Services.Monitoring.Biometrics;

public class RekognitionFaceLivenessService : IFaceLivenessService
{
    private const string LivenessPermissionPolicy =
        """{"Version":"2012-10-17","Statement":[{"Effect":"Allow","Action":["rekognition:StartFaceLivenessSession"],"Resource":"*"}]}""";

    private readonly IAmazonRekognition _rekognition;
    private readonly IAmazonSecurityTokenService _sts;
    private readonly AwsRekognitionOptions _options;

    public RekognitionFaceLivenessService(
        IAmazonRekognition rekognition, IAmazonSecurityTokenService sts, IOptions<AwsRekognitionOptions> options)
    {
        _rekognition = rekognition;
        _sts = sts;
        _options = options.Value;
    }

    public async Task<FaceLivenessSession> CreateSessionAsync(CancellationToken ct)
    {
        var response = await _rekognition.CreateFaceLivenessSessionAsync(new CreateFaceLivenessSessionRequest
        {
            ClientRequestToken = Guid.NewGuid().ToString(),
            Settings = new CreateFaceLivenessSessionRequestSettings { AuditImagesLimit = 1 }
        }, ct);

        return new FaceLivenessSession(response.SessionId, _options.Region);
    }

    public async Task<FaceLivenessOutcome> GetSessionResultAsync(string sessionId, CancellationToken ct)
    {
        var response = await _rekognition.GetFaceLivenessSessionResultsAsync(
            new GetFaceLivenessSessionResultsRequest { SessionId = sessionId }, ct);

        return new FaceLivenessOutcome(response.Status.Value, response.Confidence ?? 0f);
    }

    public async Task<ScopedAwsCredentials> AssumeLivenessRoleAsync(string sessionId, CancellationToken ct)
    {
        var sessionName = $"liveness-{sessionId}";
        if (sessionName.Length > 64) sessionName = sessionName[..64];

        var response = await _sts.AssumeRoleAsync(new AssumeRoleRequest
        {
            RoleArn = _options.LivenessRoleArn,
            RoleSessionName = sessionName,
            DurationSeconds = _options.RoleSessionDurationSeconds,
            Policy = LivenessPermissionPolicy
        }, ct);

        return new ScopedAwsCredentials(
            response.Credentials.AccessKeyId,
            response.Credentials.SecretAccessKey,
            response.Credentials.SessionToken,
            new DateTimeOffset(response.Credentials.Expiration ?? DateTime.UtcNow, TimeSpan.Zero));
    }
}
