using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Services.SharedPlatform.RateLimiting;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// Improvements task 46: the loader reads every override row and each tenant's plan through the
/// app login, under row-level security, in system mode, and its stamp changes when the data does.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class RateLimitOverrideLoaderTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private SqlConnection _admin = null!;
    private ServiceProvider _services = null!;
    private Guid _planId;
    private readonly Guid _tenantOnPlan = Guid.NewGuid();
    private readonly Guid _tenantWithoutPlan = Guid.NewGuid();
    private readonly Guid _overriddenTenant = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateDatabaseAsync();
        _admin = new SqlConnection(_database.Admin);
        await _admin.OpenAsync();
        await using (var mode = TenantRlsInterceptor.SetTenantCommand(_admin, string.Empty, "admin"))
            await mode.ExecuteNonQueryAsync();
        await RowFactory.DisableConstraintsAsync(_admin);

        await using (var plan = _admin.CreateCommand())
        {
            plan.CommandText = "SELECT TOP 1 id FROM subscription_plans";
            _planId = (Guid)(await plan.ExecuteScalarAsync())!;
        }

        await RowFactory.InsertAsync(_admin, "tenants", new Dictionary<string, object?>
        {
            ["id"] = _tenantOnPlan, ["slug"] = "on-plan", ["subscription_plan_id"] = _planId,
        });
        await RowFactory.InsertAsync(_admin, "tenants", new Dictionary<string, object?>
        {
            ["id"] = _tenantWithoutPlan, ["slug"] = "no-plan", ["subscription_plan_id"] = null,
        });

        var services = new ServiceCollection();
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<IWritableTenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped(sp =>
        {
            var tenantContext = sp.GetRequiredService<TenantContextAccessor>();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(_database.App)
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(new TenantRlsInterceptor(tenantContext))
                .Options;
            var clock = new SystemDateTimeProvider();
            return new ApplicationDbContext(
                options,
                new AuditableEntityInterceptor(new AnonymousCurrentUser(), clock),
                new SoftDeleteInterceptor(clock),
                new DomainEventDispatchInterceptor(new NoOpPublisher()),
                tenantContext);
        });
        _services = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _admin.DisposeAsync();
    }

    private async Task InsertSettingAsync(string scope, Guid? plan, Guid? tenant, string limit, int perMinute, int? burst) =>
        await RowFactory.InsertAsync(_admin, "rate_limit_settings", new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid(), ["scope"] = scope, ["subscription_plan_id"] = plan, ["tenant_id"] = tenant,
            ["limit_name"] = limit, ["per_minute"] = perMinute, ["burst"] = burst,
            ["updated_at"] = DateTimeOffset.UtcNow, ["updated_by_platform_user_id"] = Guid.NewGuid(),
        });

    [Fact]
    public async Task Loads_every_row_and_the_tenant_plans_in_system_mode()
    {
        await InsertSettingAsync("global", null, null, "Anonymous", 300, null);
        await InsertSettingAsync("plan", _planId, null, "SignedIn", 900, 150);
        await InsertSettingAsync("tenant", null, _overriddenTenant, "TenantCeiling", 20_000, 4_000);
        var loader = new EfRateLimitOverrideLoader(_services.GetRequiredService<IServiceScopeFactory>());

        var data = await loader.LoadAsync(default);

        data.Rows.Should().HaveCount(3);
        data.Rows.Should().ContainEquivalentOf(new RateLimitOverrideRow("plan", _planId, null, "SignedIn", 900, 150));
        data.Rows.Should().ContainEquivalentOf(new RateLimitOverrideRow("tenant", null, _overriddenTenant, "TenantCeiling", 20_000, 4_000));
        data.TenantPlans.Should().ContainKey(_tenantOnPlan).WhoseValue.Should().Be(_planId);
        data.TenantPlans.Should().NotContainKey(_tenantWithoutPlan);
    }

    [Fact]
    public async Task The_stamp_changes_when_a_row_or_a_plan_changes()
    {
        var loader = new EfRateLimitOverrideLoader(_services.GetRequiredService<IServiceScopeFactory>());
        var empty = await loader.GetStampAsync(default);
        empty.RowCount.Should().Be(0);
        empty.LastUpdatedAt.Should().BeNull();

        await InsertSettingAsync("global", null, null, "Anonymous", 300, null);
        var oneRow = await loader.GetStampAsync(default);
        oneRow.Should().NotBe(empty);
        oneRow.RowCount.Should().Be(1);
        (await loader.GetStampAsync(default)).Should().Be(oneRow, "nothing changed");

        await using (var update = _admin.CreateCommand())
        {
            update.CommandText = $"UPDATE tenants SET subscription_plan_id = '{_planId}' WHERE id = '{_tenantWithoutPlan}'";
            await update.ExecuteNonQueryAsync();
        }

        (await loader.GetStampAsync(default)).Should().NotBe(oneRow, "a tenant's plan changed");
    }

    [Fact]
    public async Task The_provider_built_on_the_loader_applies_the_plan_row_to_the_plans_tenants()
    {
        await InsertSettingAsync("plan", _planId, null, "SignedIn", 900, 150);
        var options = new Microsoft.Extensions.Options.OptionsMonitor<ONEVO.Application.Common.Configuration.RateLimitSettings>(
            new Microsoft.Extensions.Options.OptionsFactory<ONEVO.Application.Common.Configuration.RateLimitSettings>([], []),
            [], new Microsoft.Extensions.Options.OptionsCache<ONEVO.Application.Common.Configuration.RateLimitSettings>());
        using var baseProvider = new OptionsRateLimitSettingsProvider(
            options, Microsoft.Extensions.Logging.Abstractions.NullLogger<OptionsRateLimitSettingsProvider>.Instance);
        using var provider = new DatabaseRateLimitSettingsProvider(
            baseProvider, new EfRateLimitOverrideLoader(_services.GetRequiredService<IServiceScopeFactory>()), options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseRateLimitSettingsProvider>.Instance);

        (await provider.ReloadAsync(default)).Should().BeTrue();

        provider.GetSettings(_tenantOnPlan).Settings.SignedIn.TokenLimit.Should().Be(150);
        provider.GetSettings(_tenantWithoutPlan).Settings.SignedIn.TokenLimit.Should().Be(100, "appsettings default");
    }
}
