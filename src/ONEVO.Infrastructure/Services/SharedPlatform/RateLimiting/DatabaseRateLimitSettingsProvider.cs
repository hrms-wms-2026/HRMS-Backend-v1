using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Configuration;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.SharedPlatform.RateLimiting;

/// <summary>
/// Task 46: rate limits with the admin overrides from <c>rate_limit_settings</c> applied.
/// <para>
/// Every request is answered from one immutable in-memory state (one dictionary lookup). The
/// database is read only on a reload: at startup, after <see cref="InvalidateAsync"/>, and when
/// the 30-second check in <see cref="RateLimitSettingsRefreshService"/> sees a change.
/// </para>
/// <para>
/// Precedence: tenant row, then plan row, then global row, then appsettings (the base from
/// <see cref="OptionsRateLimitSettingsProvider"/>). <c>Enabled</c>, periods and windows always come
/// from the base. Versions only go up, and only where a result really changed, so a change to one
/// tenant resets only that tenant's counters. A failed load keeps the last good state.
/// </para>
/// </summary>
public sealed class DatabaseRateLimitSettingsProvider
    : IRateLimitSettingsProvider, IRateLimitSettingsInvalidator, IDisposable
{
    private readonly OptionsRateLimitSettingsProvider _base;
    private readonly IRateLimitOverrideLoader _loader;
    private readonly ILogger<DatabaseRateLimitSettingsProvider> _logger;
    private readonly SemaphoreSlim _reload = new(1, 1);
    private readonly IDisposable? _baseSubscription;
    private State _state;

    public DatabaseRateLimitSettingsProvider(
        OptionsRateLimitSettingsProvider baseProvider,
        IRateLimitOverrideLoader loader,
        IOptionsMonitor<RateLimitSettings> options,
        ILogger<DatabaseRateLimitSettingsProvider> logger)
    {
        _base = baseProvider;
        _loader = loader;
        _logger = logger;
        _state = State.Initial(baseProvider.GetSettings(null));

        // An appsettings reload rebuilds from the rows already loaded. It runs on another thread so
        // the base provider's own OnChange handler has applied the new values first.
        _baseSubscription = options.OnChange(changed => Task.Run(RebuildFromLastDataAsync));
    }

    /// <summary>The cheap summary taken at the last successful load, or null before the first.</summary>
    public RateLimitDataStamp? LastStamp => Volatile.Read(ref _state).Stamp;

    /// <summary>True when appsettings changed since the current state was built.</summary>
    public bool BaseChangedSinceBuild => _base.GetSettings(null).Version != Volatile.Read(ref _state).BaseVersion;

    public RateLimitSettingsSnapshot GetSettings(Guid? tenantId)
    {
        var state = Volatile.Read(ref _state);
        return tenantId is Guid id && state.Tenants.TryGetValue(id, out var own) ? own : state.Platform;
    }

    public Task InvalidateAsync(CancellationToken ct = default) => ReloadAsync(ct);

    /// <summary>Loads the rows and rebuilds. Returns false, keeping the last good state, if the load fails.</summary>
    public async Task<bool> ReloadAsync(CancellationToken ct)
    {
        await _reload.WaitAsync(ct);
        try
        {
            RateLimitOverrideData data;
            RateLimitDataStamp stamp;
            try
            {
                stamp = await _loader.GetStampAsync(ct);
                data = await _loader.LoadAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Loading rate limit overrides failed; keeping platform version {Version}",
                    Volatile.Read(ref _state).Platform.Version);
                return false;
            }

            Rebuild(data, stamp);
            return true;
        }
        finally
        {
            _reload.Release();
        }
    }

    public async Task RebuildFromLastDataAsync()
    {
        await _reload.WaitAsync();
        try
        {
            var state = Volatile.Read(ref _state);
            Rebuild(state.Data ?? RateLimitOverrideData.Empty, state.Stamp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rebuilding rate limits after an appsettings change failed");
        }
        finally
        {
            _reload.Release();
        }
    }

    // Callers hold _reload, so only one rebuild runs at a time; readers see either the old or the new state.
    private void Rebuild(RateLimitOverrideData data, RateLimitDataStamp? stamp)
    {
        var old = Volatile.Read(ref _state);
        var baseSnapshot = _base.GetSettings(null);
        var baseSettings = baseSnapshot.Settings;

        var platformSettings = Checked(RateLimitSnapshotBuilder.Platform(baseSettings, data.Rows), baseSettings, null);
        var platformPrint = RateLimitSnapshotBuilder.Fingerprint(platformSettings);
        var platform = platformPrint == old.PlatformPrint
            ? old.Platform
            : new RateLimitSettingsSnapshot(old.Platform.Version + 1, platformSettings);

        // Tenants with a row of their own or of their plan, plus every tenant that ever had its own
        // snapshot: such a tenant keeps its own, ever-rising version, so it never falls back onto a
        // partition key it already used with other limits.
        var candidates = new HashSet<Guid>(old.Tenants.Keys);
        foreach (var row in data.Rows)
        {
            if (row.TenantId is Guid tenant)
                candidates.Add(tenant);
            else if (row.SubscriptionPlanId is Guid plan)
                candidates.UnionWith(data.TenantPlans.Where(p => p.Value == plan).Select(p => p.Key));
        }

        var tenants = new Dictionary<Guid, RateLimitSettingsSnapshot>();
        var prints = new Dictionary<Guid, string>();
        foreach (var tenant in candidates)
        {
            Guid? plan = data.TenantPlans.TryGetValue(tenant, out var planId) ? planId : null;
            var settings = Checked(
                RateLimitSnapshotBuilder.Tenant(baseSettings, data.Rows, tenant, plan), platformSettings, tenant);
            var print = RateLimitSnapshotBuilder.Fingerprint(settings);
            var hadOwn = old.Tenants.TryGetValue(tenant, out var previous);

            if (!hadOwn && print == platformPrint)
                continue;

            if (hadOwn && old.TenantPrints[tenant] == print)
            {
                tenants[tenant] = previous!;
            }
            else
            {
                var version = Math.Max(previous?.Version ?? 0, Math.Max(platform.Version, old.Platform.Version)) + 1;
                tenants[tenant] = new RateLimitSettingsSnapshot(version, settings);
            }

            prints[tenant] = print;
        }

        Volatile.Write(ref _state, new State(platform, platformPrint, tenants, prints, data, stamp, baseSnapshot.Version));
        _logger.LogInformation(
            "Rate limits loaded: platform version {Version}, {TenantSnapshots} tenant snapshots, {Rows} override rows",
            platform.Version, tenants.Count, data.Rows.Count);
    }

    // A snapshot that still fails Validate() never reaches the limiter: the tenant gets the
    // platform settings, the platform gets appsettings.
    private RateLimitSettings Checked(RateLimitSettings built, RateLimitSettings fallback, Guid? tenantId)
    {
        var errors = built.Validate();
        if (errors.Count == 0)
            return built;

        _logger.LogError("Rate limit overrides gave invalid {Which} settings, using the fallback: {Errors}",
            tenantId is null ? "platform" : "tenant", string.Join(" ", errors));
        return fallback;
    }

    public void Dispose()
    {
        _baseSubscription?.Dispose();
        _reload.Dispose();
    }

    private sealed record State(
        RateLimitSettingsSnapshot Platform,
        string PlatformPrint,
        IReadOnlyDictionary<Guid, RateLimitSettingsSnapshot> Tenants,
        IReadOnlyDictionary<Guid, string> TenantPrints,
        RateLimitOverrideData? Data,
        RateLimitDataStamp? Stamp,
        long BaseVersion)
    {
        public static State Initial(RateLimitSettingsSnapshot baseSnapshot) => new(
            new RateLimitSettingsSnapshot(1, baseSnapshot.Settings),
            RateLimitSnapshotBuilder.Fingerprint(baseSnapshot.Settings),
            new Dictionary<Guid, RateLimitSettingsSnapshot>(),
            new Dictionary<Guid, string>(),
            null,
            null,
            baseSnapshot.Version);
    }
}
