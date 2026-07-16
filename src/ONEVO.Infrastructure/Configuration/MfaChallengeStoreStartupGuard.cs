using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Infrastructure.Identity;

namespace ONEVO.Infrastructure.Configuration;

/// <summary>
/// Fails startup outside Development/Test when the resolved IMfaChallengeStore is still the
/// process-local MemoryMfaChallengeStore. Process-local challenge state cannot be verified by a
/// second API instance and is lost on restart, so Production/Staging must run an approved shared
/// cache/session-backed IMfaChallengeStore, or explicitly opt in via
/// Auth:Mfa:AllowProcessLocalChallengeStore for a verified single-instance deployment.
/// </summary>
public sealed class MfaChallengeStoreStartupGuard : IHostedService
{
    public const string AllowProcessLocalConfigKey = "Auth:Mfa:AllowProcessLocalChallengeStore";

    private readonly IMfaChallengeStore _mfaChallengeStore;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MfaChallengeStoreStartupGuard> _logger;

    public MfaChallengeStoreStartupGuard(
        IMfaChallengeStore mfaChallengeStore,
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger<MfaChallengeStoreStartupGuard> logger)
    {
        _mfaChallengeStore = mfaChallengeStore;
        _environment = environment;
        _configuration = configuration;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_mfaChallengeStore is not MemoryMfaChallengeStore)
            return Task.CompletedTask;

        if (_environment.IsDevelopment() || _environment.IsEnvironment("Test"))
            return Task.CompletedTask;

        if (_configuration.GetValue<bool>(AllowProcessLocalConfigKey))
        {
            _logger.LogWarning(
                "[MFA] Process-local MemoryMfaChallengeStore is active in environment '{Environment}' " +
                "because {ConfigKey}=true. MFA challenges created on one instance cannot be verified by " +
                "another instance and are lost on restart. This override must be temporary.",
                _environment.EnvironmentName,
                AllowProcessLocalConfigKey);
            return Task.CompletedTask;
        }

        throw new InvalidOperationException(
            $"MFA challenge storage is process-local (MemoryMfaChallengeStore) but the host environment " +
            $"is '{_environment.EnvironmentName}'. Production and Staging multi-instance deployments " +
            $"require approved shared cache/session-backed MFA challenge storage. Register a shared " +
            $"IMfaChallengeStore implementation, or set '{AllowProcessLocalConfigKey}=true' as an " +
            $"explicit, temporary override only for a verified single-instance deployment.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
