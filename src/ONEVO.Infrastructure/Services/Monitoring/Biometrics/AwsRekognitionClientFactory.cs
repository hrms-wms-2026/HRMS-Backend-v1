using Amazon;
using Amazon.Rekognition;
using Amazon.SecurityToken;
using Microsoft.Extensions.Options;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;
using ONEVO.Infrastructure.Configuration;

namespace ONEVO.Infrastructure.Services.Monitoring.Biometrics;

public sealed class AwsRekognitionClientFactory : IAwsRekognitionClientFactory, IDisposable
{
    private readonly IPlatformServiceKeyResolver _keyResolver;
    private readonly AwsRekognitionOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IAmazonRekognition? _rekognition;
    private IAmazonSecurityTokenService? _sts;
    private ResolvedCredentials? _resolved;

    public AwsRekognitionClientFactory(
        IPlatformServiceKeyResolver keyResolver,
        IOptions<AwsRekognitionOptions> options)
    {
        _keyResolver = keyResolver;
        _options = options.Value;
    }

    public async Task<IAmazonRekognition> GetRekognitionAsync(CancellationToken ct)
    {
        var resolved = await ResolveAsync(ct);
        if (_rekognition is not null)
            return _rekognition;

        await _gate.WaitAsync(ct);
        try
        {
            _rekognition ??= new AmazonRekognitionClient(
                resolved.AccessKeyId,
                resolved.SecretAccessKey,
                RegionEndpoint.GetBySystemName(resolved.Region));
            return _rekognition;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IAmazonSecurityTokenService> GetStsAsync(CancellationToken ct)
    {
        var resolved = await ResolveAsync(ct);
        if (_sts is not null)
            return _sts;

        await _gate.WaitAsync(ct);
        try
        {
            _sts ??= new AmazonSecurityTokenServiceClient(
                resolved.AccessKeyId,
                resolved.SecretAccessKey,
                RegionEndpoint.GetBySystemName(resolved.Region));
            return _sts;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> GetRegionAsync(CancellationToken ct)
        => (await ResolveAsync(ct)).Region;

    public async Task<string> GetLivenessRoleArnAsync(CancellationToken ct)
    {
        var resolved = await ResolveAsync(ct);
        if (string.IsNullOrWhiteSpace(resolved.LivenessRoleArn))
            throw new InvalidOperationException("AWS Rekognition liveness role ARN is not configured.");
        return resolved.LivenessRoleArn;
    }

    private async Task<ResolvedCredentials> ResolveAsync(CancellationToken ct)
    {
        if (_resolved is not null)
            return _resolved;

        await _gate.WaitAsync(ct);
        try
        {
            if (_resolved is not null)
                return _resolved;

            var raw = await _keyResolver.ResolveActiveKeyAsync(PlatformServiceKeyCatalog.AwsRekognition, ct);
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException(
                    "AWS Rekognition is not configured. No active platform service key found.");
            }

            if (!AwsRekognitionCredentialBundle.TryParse(raw, out var bundle) || !bundle.HasAccessKeys)
            {
                throw new InvalidOperationException(
                    "AWS Rekognition credential bundle is missing required fields.");
            }

            var region = string.IsNullOrWhiteSpace(bundle.Region) ? _options.Region : bundle.Region.Trim();
            if (string.IsNullOrWhiteSpace(region))
                throw new InvalidOperationException("AWS Rekognition region is not configured.");

            var livenessArn = string.IsNullOrWhiteSpace(bundle.LivenessRoleArn)
                ? _options.LivenessRoleArn
                : bundle.LivenessRoleArn.Trim();

            _resolved = new ResolvedCredentials(
                bundle.AccessKeyId.Trim(),
                bundle.SecretAccessKey.Trim(),
                region,
                livenessArn);
            return _resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        (_rekognition as IDisposable)?.Dispose();
        (_sts as IDisposable)?.Dispose();
        _gate.Dispose();
    }

    private sealed record ResolvedCredentials(
        string AccessKeyId,
        string SecretAccessKey,
        string Region,
        string LivenessRoleArn);
}
