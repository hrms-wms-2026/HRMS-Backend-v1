using Amazon;
using Amazon.Runtime;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;

namespace ONEVO.Infrastructure.Services.SystemConfig;

public sealed record AwsIdentityProbeResult(bool Success, string Message);

/// <summary>
/// Confirms a set of AWS access keys is real and active. Seam so verification can be
/// unit-tested without calling AWS. SECURITY: the secret is never logged or echoed.
/// </summary>
public interface IAwsIdentityProbe
{
    Task<AwsIdentityProbeResult> ProbeAsync(
        string accessKeyId, string secretAccessKey, string region, CancellationToken ct);
}

/// <summary>
/// Uses STS GetCallerIdentity, which any valid key can call without IAM permissions, so a
/// success means "the credentials are valid" without granting or needing Rekognition access.
/// </summary>
public sealed class StsAwsIdentityProbe : IAwsIdentityProbe
{
    public async Task<AwsIdentityProbeResult> ProbeAsync(
        string accessKeyId, string secretAccessKey, string region, CancellationToken ct)
    {
        try
        {
            using var client = new AmazonSecurityTokenServiceClient(
                new BasicAWSCredentials(accessKeyId, secretAccessKey),
                RegionEndpoint.GetBySystemName(region));
            await client.GetCallerIdentityAsync(new GetCallerIdentityRequest(), ct);
            return new AwsIdentityProbeResult(true, "AWS credentials verified successfully.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (AmazonServiceException ex)
        {
            return new AwsIdentityProbeResult(false, $"AWS rejected the credentials ({ex.ErrorCode ?? "unknown error"}).");
        }
        catch (Exception ex)
        {
            return new AwsIdentityProbeResult(false, $"AWS verification request failed: {ex.GetType().Name}.");
        }
    }
}
