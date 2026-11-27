using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Configuration;
using ONEVO.Domain.Features.SharedPlatform.RateLimiting;
using ONEVO.Infrastructure.Services.SharedPlatform.RateLimiting;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Api.RateLimiting;

/// <summary>Task 46: rate limits from the database, answered from memory.</summary>
public sealed class DatabaseRateLimitSettingsProviderTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid PlanGold = Guid.Parse("99999999-0000-0000-0000-000000000009");

    private static RateLimitOverrideRow Global(string limit, int perMinute, int? burst = null) =>
        new(RateLimitSettingRules.GlobalScope, null, null, limit, perMinute, burst);

    private static RateLimitOverrideRow Plan(Guid plan, string limit, int perMinute, int? burst) =>
        new(RateLimitSettingRules.PlanScope, plan, null, limit, perMinute, burst);

    private static RateLimitOverrideRow Tenant(Guid tenant, string limit, int perMinute, int? burst) =>
        new(RateLimitSettingRules.TenantScope, null, tenant, limit, perMinute, burst);

    private static (DatabaseRateLimitSettingsProvider Provider, FakeLoader Loader, TestMonitor Options) Create(
        RateLimitSettings? appsettings = null)
    {
        var options = new TestMonitor(appsettings ?? new RateLimitSettings());
        var baseProvider = new OptionsRateLimitSettingsProvider(options, NullLogger<OptionsRateLimitSettingsProvider>.Instance);
        var loader = new FakeLoader();
        var provider = new DatabaseRateLimitSettingsProvider(
            baseProvider, loader, options, NullLogger<DatabaseRateLimitSettingsProvider>.Instance);
        return (provider, loader, options);
    }

    [Fact]
    public async Task Tenant_row_beats_plan_row_beats_global_row_beats_appsettings()
    {
        var (provider, loader, _) = Create();
        loader.TenantPlans[TenantA] = PlanGold;
        loader.TenantPlans[TenantB] = PlanGold;
        loader.Rows.AddRange(
        [
            Global(RateLimitSettingRules.SignedIn, 900, 150),
            Global(RateLimitSettingRules.TrayDevice, 600, 100),
            Plan(PlanGold, RateLimitSettingRules.SignedIn, 1_200, 200),
            Plan(PlanGold, RateLimitSettingRules.TrayDevice, 720, 120),
            Tenant(TenantA, RateLimitSettingRules.SignedIn, 1_800, 300),
        ]);

        await provider.ReloadAsync(default);

        var a = provider.GetSettings(TenantA).Settings;
        a.SignedIn.TokenLimit.Should().Be(300, "tenant row wins");
        a.TrayDevice.TokenLimit.Should().Be(120, "plan row beats global");
        a.TenantCeiling.TokenLimit.Should().Be(new RateLimitSettings().TenantCeiling.TokenLimit, "no row: appsettings");
        provider.GetSettings(TenantB).Settings.SignedIn.TokenLimit.Should().Be(200, "plan row beats global");
        provider.GetSettings(Guid.NewGuid()).Settings.SignedIn.TokenLimit.Should().Be(150, "global row beats appsettings");
    }

    [Fact]
    public async Task A_tenant_whose_plan_has_no_row_gets_the_global_row()
    {
        var (provider, loader, _) = Create();
        loader.TenantPlans[TenantA] = Guid.NewGuid();
        loader.Rows.AddRange([Global(RateLimitSettingRules.SignedIn, 900, 150), Plan(PlanGold, RateLimitSettingRules.SignedIn, 1_200, 200)]);

        await provider.ReloadAsync(default);

        provider.GetSettings(TenantA).Settings.SignedIn.TokenLimit.Should().Be(150);
        provider.GetSettings(TenantA).Should().BeSameAs(provider.GetSettings(null), "nothing differs from the platform snapshot");
    }

    [Fact]
    public async Task Platform_settings_ignore_plan_and_tenant_rows_and_hand_added_rows_for_platform_limits()
    {
        var (provider, loader, _) = Create();
        loader.TenantPlans[TenantA] = PlanGold;
        loader.Rows.AddRange(
        [
            Plan(PlanGold, RateLimitSettingRules.SignedIn, 1_200, 200),
            Tenant(TenantA, RateLimitSettingRules.Anonymous, 1_200, null),
            Tenant(TenantA, RateLimitSettingRules.AdminSignedIn, 2_400, 300),
            Plan(PlanGold, RateLimitSettingRules.AdminAnonymous, 600, null),
        ]);

        await provider.ReloadAsync(default);

        var defaults = new RateLimitSettings();
        var platform = provider.GetSettings(null).Settings;
        platform.SignedIn.TokenLimit.Should().Be(defaults.SignedIn.TokenLimit);
        var tenant = provider.GetSettings(TenantA).Settings;
        tenant.Anonymous.PermitLimit.Should().Be(defaults.Anonymous.PermitLimit);
        tenant.AdminSignedIn.TokenLimit.Should().Be(defaults.AdminSignedIn.TokenLimit);
        tenant.AdminAnonymous.PermitLimit.Should().Be(defaults.AdminAnonymous.PermitLimit);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Enabled_always_comes_from_appsettings(bool enabled)
    {
        var (provider, loader, _) = Create(new RateLimitSettings { Enabled = enabled });
        loader.Rows.AddRange([Global(RateLimitSettingRules.SignedIn, 900, 150), Tenant(TenantA, RateLimitSettingRules.SignedIn, 1_200, 200)]);

        await provider.ReloadAsync(default);

        provider.GetSettings(null).Settings.Enabled.Should().Be(enabled);
        provider.GetSettings(TenantA).Settings.Enabled.Should().Be(enabled);
    }

    [Fact]
    public async Task Per_minute_and_burst_convert_to_the_limiter_shapes()
    {
        var (provider, loader, _) = Create();
        loader.Rows.AddRange([Global(RateLimitSettingRules.SignedIn, 600, 100), Global(RateLimitSettingRules.Anonymous, 240)]);

        await provider.ReloadAsync(default);

        var settings = provider.GetSettings(null).Settings;
        settings.SignedIn.TokenLimit.Should().Be(100);
        settings.SignedIn.TokensPerPeriod.Should().Be(10);
        settings.SignedIn.ReplenishmentPeriodSeconds.Should().Be(1);
        settings.Anonymous.PermitLimit.Should().Be(240, "the default window is 60 s");
        settings.Anonymous.WindowSeconds.Should().Be(60);
    }

    [Fact]
    public async Task Sliding_window_conversion_uses_the_appsettings_window()
    {
        var appsettings = new RateLimitSettings();
        appsettings.Anonymous.WindowSeconds = 30;
        var (provider, loader, _) = Create(appsettings);
        loader.Rows.Add(Global(RateLimitSettingRules.Anonymous, 600));

        await provider.ReloadAsync(default);

        provider.GetSettings(null).Settings.Anonymous.PermitLimit.Should().Be(300);
    }

    [Fact]
    public async Task Rows_outside_the_bounds_are_clamped_never_zero()
    {
        var (provider, loader, _) = Create();
        loader.Rows.AddRange(
        [
            Global(RateLimitSettingRules.SignedIn, 10, 1),
            Global(RateLimitSettingRules.TrayDevice, 200_000, 50_000),
            Global(RateLimitSettingRules.AdminAnonymous, 10),
        ]);

        await provider.ReloadAsync(default);

        var settings = provider.GetSettings(null).Settings;
        var signedIn = RateLimitBounds.For(RateLimitSettingRules.SignedIn);
        settings.SignedIn.TokenLimit.Should().Be(signedIn.MinBurst);
        settings.SignedIn.TokensPerPeriod.Should().Be(signedIn.MinPerMinute / 60);
        var tray = RateLimitBounds.For(RateLimitSettingRules.TrayDevice);
        settings.TrayDevice.TokenLimit.Should().Be(tray.MaxBurst);
        settings.TrayDevice.TokensPerPeriod.Should().Be(tray.MaxPerMinute / 60);
        settings.AdminAnonymous.PermitLimit.Should().Be(RateLimitBounds.For(RateLimitSettingRules.AdminAnonymous).MinPerMinute);
    }

    [Fact]
    public async Task The_base_settings_object_is_never_changed()
    {
        var appsettings = new RateLimitSettings();
        var (provider, loader, _) = Create(appsettings);
        loader.Rows.Add(Global(RateLimitSettingRules.SignedIn, 900, 150));

        await provider.ReloadAsync(default);

        appsettings.SignedIn.TokenLimit.Should().Be(new RateLimitSettings().SignedIn.TokenLimit);
        provider.GetSettings(null).Settings.Should().NotBeSameAs(appsettings);
    }

    [Fact]
    public async Task Changing_one_tenant_bumps_only_that_tenants_version()
    {
        var (provider, loader, _) = Create();
        loader.Rows.AddRange([Tenant(TenantA, RateLimitSettingRules.SignedIn, 900, 150), Tenant(TenantB, RateLimitSettingRules.SignedIn, 900, 150)]);
        await provider.ReloadAsync(default);
        var (platform, a, b) = Versions(provider);

        loader.Rows[0] = Tenant(TenantA, RateLimitSettingRules.SignedIn, 1_200, 200);
        await provider.ReloadAsync(default);

        provider.GetSettings(TenantA).Version.Should().BeGreaterThan(a);
        provider.GetSettings(TenantB).Version.Should().Be(b);
        provider.GetSettings(null).Version.Should().Be(platform);
    }

    [Fact]
    public async Task Changing_a_global_row_bumps_the_platform_and_every_tenant_that_inherits_it()
    {
        var (provider, loader, _) = Create();
        loader.Rows.AddRange([Global(RateLimitSettingRules.TrayDevice, 600, 100), Tenant(TenantA, RateLimitSettingRules.SignedIn, 900, 150)]);
        await provider.ReloadAsync(default);
        var (platform, a, _) = Versions(provider);

        loader.Rows[0] = Global(RateLimitSettingRules.TrayDevice, 720, 120);
        await provider.ReloadAsync(default);

        provider.GetSettings(null).Version.Should().BeGreaterThan(platform);
        provider.GetSettings(TenantA).Version.Should().BeGreaterThan(a);
        provider.GetSettings(TenantA).Settings.TrayDevice.TokenLimit.Should().Be(120);
    }

    [Fact]
    public async Task A_reload_with_no_change_keeps_every_version()
    {
        var (provider, loader, _) = Create();
        loader.TenantPlans[TenantB] = PlanGold;
        loader.Rows.AddRange(
        [
            Global(RateLimitSettingRules.SignedIn, 900, 150),
            Plan(PlanGold, RateLimitSettingRules.SignedIn, 1_200, 200),
            Tenant(TenantA, RateLimitSettingRules.SignedIn, 1_800, 300),
        ]);
        await provider.ReloadAsync(default);
        var before = Versions(provider);
        var platformSnapshot = provider.GetSettings(null);

        await provider.ReloadAsync(default);

        Versions(provider).Should().Be(before);
        provider.GetSettings(null).Should().BeSameAs(platformSnapshot);
    }

    [Fact]
    public async Task Removing_a_tenants_last_override_still_raises_its_version()
    {
        var (provider, loader, _) = Create();
        loader.Rows.Add(Tenant(TenantA, RateLimitSettingRules.SignedIn, 900, 150));
        await provider.ReloadAsync(default);
        var before = provider.GetSettings(TenantA).Version;

        loader.Rows.Clear();
        await provider.ReloadAsync(default);

        var after = provider.GetSettings(TenantA);
        after.Version.Should().BeGreaterThan(before);
        after.Settings.SignedIn.TokenLimit.Should().Be(new RateLimitSettings().SignedIn.TokenLimit);

        loader.Rows.Add(Tenant(TenantA, RateLimitSettingRules.SignedIn, 900, 150));
        await provider.ReloadAsync(default);
        provider.GetSettings(TenantA).Version.Should().BeGreaterThan(after.Version, "a tenant's version never goes back");
    }

    [Fact]
    public async Task A_tenants_first_override_gets_a_version_above_the_platform_one()
    {
        var (provider, loader, _) = Create();
        loader.Rows.Add(Global(RateLimitSettingRules.SignedIn, 900, 150));
        await provider.ReloadAsync(default);
        var platform = provider.GetSettings(null).Version;

        loader.Rows.Add(Tenant(TenantA, RateLimitSettingRules.SignedIn, 1_200, 200));
        await provider.ReloadAsync(default);

        provider.GetSettings(TenantA).Version.Should().BeGreaterThan(platform);
    }

    [Fact]
    public async Task A_failed_load_keeps_the_last_good_snapshots()
    {
        var (provider, loader, _) = Create();
        loader.Rows.Add(Tenant(TenantA, RateLimitSettingRules.SignedIn, 900, 150));
        await provider.ReloadAsync(default);
        var good = provider.GetSettings(TenantA);

        loader.Fail = true;
        (await provider.ReloadAsync(default)).Should().BeFalse();

        provider.GetSettings(TenantA).Should().BeSameAs(good);
    }

    [Fact]
    public async Task A_failed_first_load_answers_with_appsettings()
    {
        var (provider, loader, _) = Create();
        loader.Fail = true;

        (await provider.ReloadAsync(default)).Should().BeFalse();

        provider.GetSettings(TenantA).Settings.SignedIn.TokenLimit.Should().Be(new RateLimitSettings().SignedIn.TokenLimit);
        provider.GetSettings(TenantA).Settings.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task An_appsettings_change_rebuilds_from_the_loaded_rows()
    {
        var (provider, loader, options) = Create();
        loader.Rows.Add(Tenant(TenantA, RateLimitSettingRules.SignedIn, 900, 150));
        await provider.ReloadAsync(default);
        var (platform, a, _) = Versions(provider);
        var loads = loader.Loads;

        var changed = new RateLimitSettings();
        changed.TrayDevice.TokenLimit = 77;
        options.Change(changed);
        await provider.RebuildFromLastDataAsync();

        loader.Loads.Should().Be(loads, "no database read for an appsettings change");
        provider.GetSettings(null).Version.Should().BeGreaterThan(platform);
        provider.GetSettings(null).Settings.TrayDevice.TokenLimit.Should().Be(77);
        provider.GetSettings(TenantA).Version.Should().BeGreaterThan(a);
        provider.GetSettings(TenantA).Settings.SignedIn.TokenLimit.Should().Be(150, "the tenant row still applies");
    }

    [Fact]
    public async Task The_30_second_check_reloads_only_when_the_data_changed()
    {
        var (provider, loader, _) = Create();
        var refresh = new RateLimitSettingsRefreshService(provider, loader, NullLogger<RateLimitSettingsRefreshService>.Instance);
        await provider.ReloadAsync(default);
        var loads = loader.Loads;

        await refresh.CheckOnceAsync(default);
        loader.Loads.Should().Be(loads);

        loader.Rows.Add(Tenant(TenantA, RateLimitSettingRules.SignedIn, 900, 150));
        await refresh.CheckOnceAsync(default);
        loader.Loads.Should().Be(loads + 1);
        provider.GetSettings(TenantA).Settings.SignedIn.TokenLimit.Should().Be(150);

        loader.TenantPlans[TenantB] = PlanGold;
        await refresh.CheckOnceAsync(default);
        loader.Loads.Should().Be(loads + 2, "a tenant's plan changed");
    }

    private static (long Platform, long A, long B) Versions(DatabaseRateLimitSettingsProvider provider) =>
        (provider.GetSettings(null).Version, provider.GetSettings(TenantA).Version, provider.GetSettings(TenantB).Version);

    internal sealed class FakeLoader : IRateLimitOverrideLoader
    {
        public List<RateLimitOverrideRow> Rows { get; } = [];
        public Dictionary<Guid, Guid> TenantPlans { get; } = [];
        public bool Fail { get; set; }
        public int Loads { get; private set; }

        public Task<RateLimitOverrideData> LoadAsync(CancellationToken ct)
        {
            if (Fail) throw new InvalidOperationException("database down");
            Loads++;
            return Task.FromResult(new RateLimitOverrideData(Rows.ToList(), new Dictionary<Guid, Guid>(TenantPlans)));
        }

        // Stands in for COUNT/MAX(updated_at)/plan hash: any change to the rows changes the stamp.
        public Task<RateLimitDataStamp> GetStampAsync(CancellationToken ct)
        {
            if (Fail) throw new InvalidOperationException("database down");
            var rows = string.Join("|", Rows);
            var plans = string.Join("|", TenantPlans.OrderBy(p => p.Key));
            return Task.FromResult(new RateLimitDataStamp(Rows.Count, null, HashCode.Combine(rows, plans)));
        }
    }

    internal sealed class TestMonitor(RateLimitSettings value) : IOptionsMonitor<RateLimitSettings>
    {
        private readonly List<Action<RateLimitSettings, string?>> _listeners = [];

        public RateLimitSettings CurrentValue { get; private set; } = value;

        public RateLimitSettings Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<RateLimitSettings, string?> listener)
        {
            _listeners.Add(listener);
            return new Unsubscribe(() => _listeners.Remove(listener));
        }

        public void Change(RateLimitSettings next)
        {
            CurrentValue = next;
            foreach (var listener in _listeners.ToList())
                listener(next, null);
        }

        private sealed class Unsubscribe(Action action) : IDisposable
        {
            public void Dispose() => action();
        }
    }
}
