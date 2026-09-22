using Amazon.Rekognition;
using Amazon.SecurityToken;
using ONEVO.Infrastructure.Services.Monitoring.Biometrics;

namespace ONEVO.Tests.Unit.Features.Monitoring.Biometrics;

internal sealed class StubRekognitionClientFactory : IAwsRekognitionClientFactory
{
    private readonly IAmazonRekognition _rekognition;

    public StubRekognitionClientFactory(IAmazonRekognition rekognition) => _rekognition = rekognition;

    public Task<IAmazonRekognition> GetRekognitionAsync(CancellationToken ct) =>
        Task.FromResult(_rekognition);

    public Task<IAmazonSecurityTokenService> GetStsAsync(CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<string> GetRegionAsync(CancellationToken ct) =>
        Task.FromResult("us-east-1");

    public Task<string> GetLivenessRoleArnAsync(CancellationToken ct) =>
        Task.FromResult("arn:aws:iam::123456789012:role/liveness");
}
