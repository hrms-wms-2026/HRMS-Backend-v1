using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Infrastructure.Identity;

namespace ONEVO.Infrastructure.Configuration;

/// <summary>
/// Fails startup outside Development/Test when the resolved IMfaChallengeStore is still the
/// process-local MemoryMfaChallengeStore. The normal runtime implementation is the
/// PostgreSQL-backed mfa_challenges table (PostgresMfaChallengeStore); the memory store is only a
/// local development/test fallback. Production/Staging may run the memory store only via an
/// explicit, temporary Auth:Mfa:AllowProcessLocalChallengeStore opt-in for a verified
/// single-instance deployment.
/// </summary>
public sealed class MfaChallengeStoreStartupGuard : IHostedService
{
    public const string AllowProcessLocalConfigKey = "Auth:Mfa:AllowProcessLocalChallengeStore";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MfaChallengeStoreStartupGuard> _logger;

    public MfaChallengeStoreStartupGuard(
        IServiceScopeFactory scopeFactory,
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger<MfaChallengeStoreStartupGuard> logger)
    {
        _scopeFactory = scopeFactory;
        _environment = environment;
        _configuration = configuration;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // IMfaChallengeStore is scoped (PostgresMfaChallengeStore depends on the DbContext),
        // so it must be resolved from a scope rather than injected into this singleton.
        using var scope = _scopeFactory.CreateScope();
        var mfaChallengeStore = scope.ServiceProvider.GetRequiredService<IMfaChallengeStore>();

        if (mfaChallengeStore is not MemoryMfaChallengeStore)
            return Task.CompletedTask;

        if (_environment.IsDevelopment() || _environment.IsEnvironment("Test"))
            return Task.CompletedTask;

        if (_configuration.GetValue<bool>(AllowProcessLocalConfigKey))
        {
            _logger.LogWarning(
                "[MFA] Process-local MemoryMfaChallengeStore is active in environment '{Environment}' " +
                "because {ConfigKey}=true. MFA challenges created on one instance cannot be verified by " +
                "another instance and are lost on restart. The normal runtime implementation is the " +
                "PostgreSQL-backed mfa_challenges table. This override must be temporary.",
                _environment.EnvironmentName,
                AllowProcessLocalConfigKey);
            return Task.CompletedTask;
        }

        throw new InvalidOperationException(
            $"MFA challenge storage is process-local (MemoryMfaChallengeStore) but the host environment " +
            $"is '{_environment.EnvironmentName}'. The expected normal implementation is the " +
            $"PostgreSQL-backed mfa_challenges table (PostgresMfaChallengeStore). Register the " +
            $"PostgreSQL-backed IMfaChallengeStore, or set '{AllowProcessLocalConfigKey}=true' as an " +
            $"explicit, temporary override only for a verified single-instance deployment.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
