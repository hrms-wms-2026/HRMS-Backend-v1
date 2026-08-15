using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Microsoft.Extensions.Options;
using ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Biometrics;

public class AwsRekognitionBiometricVerificationProvider : IBiometricVerificationProvider
{
    private const float CompareFacesSimilarityThreshold = 90.0f;

    private readonly IAmazonRekognition _rekognition;
    private readonly IAmazonSecurityTokenService _sts;
    private readonly BiometricProviderOptions _options;

    public AwsRekognitionBiometricVerificationProvider(
        IAmazonRekognition rekognition,
        IAmazonSecurityTokenService sts,
        IOptions<BiometricProviderOptions> options)
    {
        _rekognition = rekognition;
        _sts = sts;
        _options = options.Value;
    }

    public async Task<FaceLivenessSessionCreated> CreateLivenessSessionAsync(
        CreateLivenessSessionRequest request, CancellationToken ct)
    {
        var kmsKeyId = string.IsNullOrWhiteSpace(request.KmsKeyId)
            ? _options.KmsKeyId
            : request.KmsKeyId;

        var response = await _rekognition.CreateFaceLivenessSessionAsync(new Amazon.Rekognition.Model.CreateFaceLivenessSessionRequest
        {
            Settings = new CreateFaceLivenessSessionRequestSettings
            {
                AuditImagesLimit = 4
            },
            KmsKeyId = string.IsNullOrWhiteSpace(kmsKeyId) ? null : kmsKeyId
        }, ct);

        return new FaceLivenessSessionCreated(response.SessionId);
    }

    public async Task<FaceLivenessSessionResult> GetLivenessSessionResultAsync(
        string awsSessionId, CancellationToken ct)
    {
        var response = await _rekognition.GetFaceLivenessSessionResultsAsync(
            new GetFaceLivenessSessionResultsRequest { SessionId = awsSessionId }, ct);

        byte[]? referenceBytes = null;
        if (response.ReferenceImage?.Bytes is { } stream)
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            referenceBytes = ms.ToArray();
        }

        return new FaceLivenessSessionResult(
            response.Status?.Value ?? "UNKNOWN",
            response.Confidence,
            referenceBytes);
    }

    public async Task<FaceMatchResult> CompareFacesAsync(
        byte[] sourceImageBytes, byte[] targetImageBytes, CancellationToken ct)
    {
        using var sourceStream = new MemoryStream(sourceImageBytes);
        using var targetStream = new MemoryStream(targetImageBytes);

        var response = await _rekognition.CompareFacesAsync(new CompareFacesRequest
        {
            SourceImage = new Image { Bytes = sourceStream },
            TargetImage = new Image { Bytes = targetStream },
            SimilarityThreshold = CompareFacesSimilarityThreshold
        }, ct);

        var bestMatch = response.FaceMatches.OrderByDescending(m => m.Similarity).FirstOrDefault();
        return bestMatch is null
            ? new FaceMatchResult(false, 0)
            : new FaceMatchResult(true, bestMatch.Similarity ?? 0);
    }

    public async Task<ScopedCaptureCredentials> IssueScopedCaptureCredentialsAsync(
        string awsSessionId, CancellationToken ct)
    {
        var response = await _sts.AssumeRoleAsync(new AssumeRoleRequest
        {
            RoleArn = _options.CaptureRoleArn,
            RoleSessionName = $"face-liveness-{awsSessionId}",
            DurationSeconds = _options.CaptureCredentialsDurationSeconds
        }, ct);

        var creds = response.Credentials;
        return new ScopedCaptureCredentials(
            creds.AccessKeyId,
            creds.SecretAccessKey,
            creds.SessionToken,
            _options.Region,
            creds.Expiration is { } expiration
                ? new DateTimeOffset(DateTime.SpecifyKind(expiration, DateTimeKind.Utc))
                : DateTimeOffset.UtcNow.AddSeconds(_options.CaptureCredentialsDurationSeconds));
    }
}
