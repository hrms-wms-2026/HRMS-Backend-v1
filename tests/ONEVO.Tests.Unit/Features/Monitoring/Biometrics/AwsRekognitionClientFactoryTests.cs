using Microsoft.Extensions.Options;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.Services.Monitoring.Biometrics;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Biometrics;

public class AwsRekognitionClientFactoryTests
{
    private sealed class FakeResolver : IPlatformServiceKeyResolver
    {
        public string? RawBundle { get; set; }

        public Task<string?> ResolveActiveKeyAsync(string serviceKey, CancellationToken ct)
        {
            Assert.Equal(PlatformServiceKeyCatalog.AwsRekognition, serviceKey);
            return Task.FromResult(RawBundle);
        }

        public Task<TransactionalEmailProviderResolution> ResolveActiveTransactionalEmailProviderAsync(
            CancellationToken ct)
            => throw new NotSupportedException();
    }

    private static AwsRekognitionClientFactory Create(string? raw) =>
        new(
            new FakeResolver { RawBundle = raw },
            Options.Create(new AwsRekognitionOptions
            {
                Region = "us-east-1",
                LivenessRoleArn = "arn:aws:iam::123456789012:role/liveness"
            }));

    [Fact]
    public async Task MissingServiceKey_ThrowsWithoutLeakingSecrets()
    {
        var factory = Create(null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.GetRekognitionAsync(CancellationToken.None));

        Assert.Contains("not configured", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpaqueString_ThrowsMissingFields()
    {
        var factory = Create("AKIAEXAMPLE");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.GetRekognitionAsync(CancellationToken.None));

        Assert.Contains("missing required fields", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AKIAEXAMPLE", ex.Message);
    }

    [Fact]
    public async Task ValidBundle_UsesRegionFromJson()
    {
        var factory = Create("""
            {
              "accessKeyId": "AKIAEXAMPLEKEY0001",
              "secretAccessKey": "secret-access-key-value",
              "region": "ap-south-1",
              "livenessRoleArn": "arn:aws:iam::123456789012:role/face-liveness"
            }
            """);

        var region = await factory.GetRegionAsync(CancellationToken.None);
        var role = await factory.GetLivenessRoleArnAsync(CancellationToken.None);

        Assert.Equal("ap-south-1", region);
        Assert.Equal("arn:aws:iam::123456789012:role/face-liveness", role);
    }
}
