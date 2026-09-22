using Amazon;
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon.Runtime;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Microsoft.Extensions.Logging;

namespace ONEVO.Infrastructure.Services.Monitoring.Biometrics;

public sealed record AwsRekognitionProbeResult(
    bool Success,
    string Message,
    string? Identity,
    string? Region);

/// <summary>
/// Live AWS check for a Rekognition credential bundle. Uses STS GetCallerIdentity
/// (who the key is) then a tiny DetectFaces call (Rekognition is reachable).
/// SECURITY: never logs access keys or secrets.
/// </summary>
public interface IAwsRekognitionConnectionProbe
{
    Task<AwsRekognitionProbeResult> ProbeAsync(
        string accessKeyId, string secretAccessKey, string region, CancellationToken ct);
}

public sealed class AwsRekognitionConnectionProbe : IAwsRekognitionConnectionProbe
{
    private readonly ILogger<AwsRekognitionConnectionProbe> _logger;

    public AwsRekognitionConnectionProbe(ILogger<AwsRekognitionConnectionProbe> logger)
        => _logger = logger;

    public async Task<AwsRekognitionProbeResult> ProbeAsync(
        string accessKeyId, string secretAccessKey, string region, CancellationToken ct)
    {
        RegionEndpoint endpoint;
        try
        {
            endpoint = RegionEndpoint.GetBySystemName(region);
        }
        catch (Exception)
        {
            return new AwsRekognitionProbeResult(false, $"Unknown AWS region '{region}'.", null, region);
        }

        string? identity;
        try
        {
            using var sts = new AmazonSecurityTokenServiceClient(accessKeyId, secretAccessKey, endpoint);
            var caller = await sts.GetCallerIdentityAsync(new GetCallerIdentityRequest(), ct);
            identity = IdentityName(caller.Arn) ?? caller.Account;
        }
        catch (AmazonServiceException ex)
        {
            _logger.LogWarning(
                "AWS STS GetCallerIdentity failed during Rekognition verification. ErrorCode={ErrorCode} StatusCode={StatusCode}",
                ex.ErrorCode,
                (int)ex.StatusCode);
            return new AwsRekognitionProbeResult(
                false,
                "AWS rejected the credentials. Check Access Key ID, Secret Access Key, and region.",
                null,
                region);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                "AWS STS GetCallerIdentity failed during Rekognition verification. ExceptionType={ExceptionType}",
                ex.GetType().Name);
            return new AwsRekognitionProbeResult(
                false,
                "Could not reach AWS to verify the Rekognition credentials.",
                null,
                region);
        }

        try
        {
            using var rekognition = new AmazonRekognitionClient(accessKeyId, secretAccessKey, endpoint);
            await rekognition.DetectFacesAsync(new DetectFacesRequest
            {
                Image = new Image { Bytes = new MemoryStream([0xFF, 0xD8, 0xFF, 0xD9]) }
            }, ct);
        }
        catch (InvalidImageFormatException)
        {
            // Credentials can call Rekognition; the probe image is intentionally not a face.
        }
        catch (InvalidParameterException)
        {
            // Same: Rekognition accepted the call and rejected the dummy bytes.
        }
        catch (AmazonServiceException ex)
        {
            _logger.LogWarning(
                "AWS DetectFaces failed during Rekognition verification. ErrorCode={ErrorCode} StatusCode={StatusCode}",
                ex.ErrorCode,
                (int)ex.StatusCode);
            return new AwsRekognitionProbeResult(
                false,
                "AWS credentials are valid but Rekognition is not allowed for this key.",
                identity,
                region);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                "AWS DetectFaces failed during Rekognition verification. ExceptionType={ExceptionType}",
                ex.GetType().Name);
            return new AwsRekognitionProbeResult(
                false,
                "Could not reach Amazon Rekognition in this region.",
                identity,
                region);
        }

        return new AwsRekognitionProbeResult(
            true,
            "Connected to Amazon Rekognition.",
            identity,
            region);
    }

    private static string? IdentityName(string? arn)
    {
        if (string.IsNullOrWhiteSpace(arn))
            return null;
        var slash = arn.LastIndexOf('/');
        return slash >= 0 && slash < arn.Length - 1 ? arn[(slash + 1)..] : arn;
    }
}
