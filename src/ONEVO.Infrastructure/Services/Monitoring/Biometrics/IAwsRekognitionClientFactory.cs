using Amazon.Rekognition;
using Amazon.SecurityToken;

namespace ONEVO.Infrastructure.Services.Monitoring.Biometrics;

/// <summary>
/// Builds AWS Rekognition/STS clients from the active aws_rekognition platform service key.
/// Never reads AccessKey/SecretKey from appsettings or the process environment.
/// </summary>
public interface IAwsRekognitionClientFactory
{
    Task<IAmazonRekognition> GetRekognitionAsync(CancellationToken ct);

    Task<IAmazonSecurityTokenService> GetStsAsync(CancellationToken ct);

    Task<string> GetRegionAsync(CancellationToken ct);

    Task<string> GetLivenessRoleArnAsync(CancellationToken ct);
}
