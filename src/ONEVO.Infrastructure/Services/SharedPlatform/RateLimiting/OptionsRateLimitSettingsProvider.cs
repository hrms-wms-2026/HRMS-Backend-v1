using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Configuration;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.SharedPlatform.RateLimiting;

/// <summary>
/// Configuration-backed <see cref="IRateLimitSettingsProvider"/>: the same limits for every
/// tenant, taken from the <c>RateLimiting</c> section and re-read when configuration reloads.
/// A reload that changes any value bumps the version; an invalid reload is logged and ignored, so
/// the limiter keeps the last good limits instead of failing requests.
/// </summary>
public sealed class OptionsRateLimitSettingsProvider : IRateLimitSettingsProvider, IDisposable
{
    private readonly ILogger<OptionsRateLimitSettingsProvider> _logger;
    private readonly IDisposable? _subscription;
    private readonly object _gate = new();
    private RateLimitSettingsSnapshot _current;
    private string _currentFingerprint;

    public OptionsRateLimitSettingsProvider(
        IOptionsMonitor<RateLimitSettings> options,
        ILogger<OptionsRateLimitSettingsProvider> logger)
    {
        _logger = logger;

        var initial = options.CurrentValue;
        var errors = initial.Validate();
        if (errors.Count > 0)
            throw new OptionsValidationException(
                RateLimitSettings.SectionName, typeof(RateLimitSettings), errors);

        _current = new RateLimitSettingsSnapshot(1, initial);
        _currentFingerprint = Fingerprint(initial);
        _subscription = options.OnChange(Apply);
    }

    public RateLimitSettingsSnapshot GetSettings(Guid? tenantId) => Volatile.Read(ref _current);

    private void Apply(RateLimitSettings updated)
    {
        var errors = updated.Validate();
        if (errors.Count > 0)
        {
            _logger.LogError(
                "Ignoring reloaded {Section} settings, keeping version {Version}: {Errors}",
                RateLimitSettings.SectionName, _current.Version, string.Join(" ", errors));
            return;
        }

        lock (_gate)
        {
            var fingerprint = Fingerprint(updated);
            if (fingerprint == _currentFingerprint)
                return;

            var next = new RateLimitSettingsSnapshot(_current.Version + 1, updated);
            _currentFingerprint = fingerprint;
            Volatile.Write(ref _current, next);
            _logger.LogInformation(
                "{Section} settings changed; now version {Version}", RateLimitSettings.SectionName, next.Version);
        }
    }

    private static string Fingerprint(RateLimitSettings settings) => JsonSerializer.Serialize(settings);

    public void Dispose() => _subscription?.Dispose();
}
