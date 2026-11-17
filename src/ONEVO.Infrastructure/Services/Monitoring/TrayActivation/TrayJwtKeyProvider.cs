using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.Monitoring.TrayActivation;

/// <summary>
/// The one source of tray device-token keys (SEC-15). The token issuer signs with it and
/// "TrayDeviceScheme" resolves validation keys from it, so both always see the same key set,
/// including the Development/Test throwaway key.
/// </summary>
public interface ITrayJwtKeyProvider
{
    /// <summary>ES256 credentials for the active key; the kid goes into every new token's header.</summary>
    SigningCredentials SigningCredentials { get; }

    /// <summary>ES256, plus HS256 only when a legacy window is configured.</summary>
    IReadOnlyList<string> ValidAlgorithms { get; }

    /// <summary>
    /// Keys for one incoming token. A token with a kid gets only the listed key of that kid. A token
    /// without a kid gets the legacy HS256 key only if it is HS256 and the legacy window is still open
    /// right now. Anything else gets no key and fails validation.
    /// </summary>
    IEnumerable<SecurityKey> ResolveValidationKeys(SecurityToken token, string? kid);
}

public sealed class TrayJwtKeyProvider : ITrayJwtKeyProvider
{
    private readonly TrayJwtKeySet _keys;
    private readonly TimeProvider _time;

    public TrayJwtKeyProvider(
        IConfiguration configuration,
        IHostEnvironment environment,
        TimeProvider time,
        ILogger<TrayJwtKeyProvider> logger)
    {
        _time = time;
        _keys = TrayJwtKeyLoader.Load(configuration, environment.EnvironmentName);

        SigningCredentials = new SigningCredentials(_keys.ActiveKey, SecurityAlgorithms.EcdsaSha256);
        ValidAlgorithms = _keys.LegacyKey is null
            ? [SecurityAlgorithms.EcdsaSha256]
            : [SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.HmacSha256];

        if (_keys.IsEphemeral)
        {
            logger.LogWarning(
                "Tray device tokens are signed with a throwaway development key {Kid}. Tokens stop validating " +
                "when the API restarts; trays recover with one refresh. Never use this outside Development/Test",
                _keys.ActiveKey.KeyId);
        }

        if (_keys.LegacyUntil is { } until)
        {
            logger.LogWarning(
                "Legacy HS256 tray tokens are accepted until {LegacyUntil:o} ({Setting}). Remove the setting and " +
                "TrayApp:Jwt:Secret after that time",
                until, TrayJwtKeyLoader.LegacyUntilKey);
        }
    }

    public SigningCredentials SigningCredentials { get; }

    public IReadOnlyList<string> ValidAlgorithms { get; }

    public IEnumerable<SecurityKey> ResolveValidationKeys(SecurityToken token, string? kid)
    {
        if (!string.IsNullOrEmpty(kid))
        {
            return _keys.ValidationKeys.TryGetValue(kid, out var key) ? [key] : [];
        }

        if (_keys.LegacyKey is not null
            && _time.GetUtcNow() < _keys.LegacyUntil
            && string.Equals(AlgorithmOf(token), SecurityAlgorithms.HmacSha256, StringComparison.Ordinal))
        {
            return [_keys.LegacyKey];
        }

        return [];
    }

    private static string? AlgorithmOf(SecurityToken token) => token switch
    {
        JsonWebToken jwt => jwt.Alg,
        JwtSecurityToken jwt => jwt.Header.Alg,
        _ => null
    };
}

public static class TrayJwtSigningServiceCollectionExtensions
{
    /// <summary>Registers the tray key provider and token issuer as singletons (one key set per process).</summary>
    public static IServiceCollection AddTrayJwtSigning(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITrayJwtKeyProvider, TrayJwtKeyProvider>();
        services.TryAddSingleton<ITrayTokenService, TrayTokenService>();
        return services;
    }
}
