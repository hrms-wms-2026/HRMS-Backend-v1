using Amazon.Rekognition.Model;
using Amazon.SecurityToken.Model;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Configuration;

namespace ONEVO.Infrastructure.Services.Monitoring.Biometrics;

public class RekognitionFaceLivenessService : IFaceLivenessService
{
    private const string LivenessPermissionPolicy =
        """{"Version":"2012-10-17","Statement":[{"Effect":"Allow","Action":["rekognition:StartFaceLivenessSession"],"Resource":"*"}]}""";

    private readonly IAwsRekognitionClientFactory _clients;
    private readonly AwsRekognitionOptions _options;

    public RekognitionFaceLivenessService(
        IAwsRekognitionClientFactory clients, IOptions<AwsRekognitionOptions> options)
    {
        _clients = clients;
        _options = options.Value;
    }

    public async Task<FaceLivenessSession> CreateSessionAsync(CancellationToken ct)
    {
        var rekognition = await _clients.GetRekognitionAsync(ct);
        var region = await _clients.GetRegionAsync(ct);
        var response = await rekognition.CreateFaceLivenessSessionAsync(new CreateFaceLivenessSessionRequest
        {
            ClientRequestToken = Guid.NewGuid().ToString(),
            Settings = new CreateFaceLivenessSessionRequestSettings { AuditImagesLimit = 1 }
        }, ct);

        return new FaceLivenessSession(response.SessionId, region);
    }

    public async Task<FaceLivenessOutcome> GetSessionResultAsync(string sessionId, CancellationToken ct)
    {
        var rekognition = await _clients.GetRekognitionAsync(ct);
        var response = await rekognition.GetFaceLivenessSessionResultsAsync(
            new GetFaceLivenessSessionResultsRequest { SessionId = sessionId }, ct);

        return new FaceLivenessOutcome(
            response.Status.Value,
            response.Confidence ?? 0f,
            response.ReferenceImage?.Bytes);
    }

    public async Task<ScopedAwsCredentials> AssumeLivenessRoleAsync(string sessionId, CancellationToken ct)
    {
        var sessionName = $"liveness-{sessionId}";
        if (sessionName.Length > 64) sessionName = sessionName[..64];

        var sts = await _clients.GetStsAsync(ct);
        var roleArn = await _clients.GetLivenessRoleArnAsync(ct);
        var response = await sts.AssumeRoleAsync(new AssumeRoleRequest
        {
            RoleArn = roleArn,
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
