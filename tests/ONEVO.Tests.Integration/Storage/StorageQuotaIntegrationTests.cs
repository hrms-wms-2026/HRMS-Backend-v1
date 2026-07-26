using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.Quota.Helpers;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Features.Storage.Quota.Entities;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.ModuleCatalog;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Subscription;
using ONEVO.Infrastructure.Persistence.Repositories.Storage.Quota;
using ONEVO.Infrastructure.Services.Storage.Quota;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Storage;

/// <summary>
/// Proves the storage quota check against a real PostgreSQL database: the
/// AddTenantStorageStats migration applies, tenant_storage_stats rows are read
/// per tenant, subscription-derived limits resolve from real
/// tenant_subscriptions + module_catalog rows, and one tenant's rows never
/// influence another tenant's decision. Requires Docker (Testcontainers).
/// </summary>
public sealed class StorageQuotaIntegrationTests : IAsyncLifetime
{
    private const long Gb = 1024L * 1024L * 1024L;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_storage_quota_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();

    private string _connectionString = string.Empty;
    private Guid _tenantAId;
    private Guid _tenantBId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(_connectionString);

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var tenantA = NewTenant("Quota Tenant A", "quota-tenant-a");
        var tenantB = NewTenant("Quota Tenant B", "quota-tenant-b");
        _tenantAId = tenantA.Id;
        _tenantBId = tenantB.Id;

        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = "Quota Test Plan",
            Code = "quota-test-plan",
            Tier = "standard",
            CompanySizeRange = "51-200",
            PricingUnit = "per_employee",
            Currency = "USD"
        };

        var module = new ModuleCatalogItem
        {
            ModuleKey = "core_hr",
            Name = "Core HR",
            Pillar = "hr",
            Phase = "phase_1",
            PricingUnit = "per_employee",
            PricingReference = "[]",
            AiTokenReference = "[]",
            IsStorageConsuming = true,
            IsActive = true,
            StorageReference =
                """[{"min_employees":51,"max_employees":200,"storage_gb":100}]"""
        };

        // Only tenant A has an active subscription; tenant B has nothing.
        var subscriptionA = new TenantSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA.Id,
            PlanId = plan.Id,
            Status = "active",
            BillingCycle = "monthly",
            CommercialModel = "subscription",
            BillingCurrency = "USD",
            CompanySizeRange = "51-200",
            SelectedModulesJson = """["core_hr"]""",
            CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow),
            CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            ContractStartDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };

        // Tenant A has used 99 GB; tenant B has a huge counter that must never
        // leak into tenant A's decision.
        var statsA = new TenantStorageStats { TenantId = tenantA.Id, UsedR2Bytes = 99 * Gb };
        var statsB = new TenantStorageStats { TenantId = tenantB.Id, UsedR2Bytes = 900 * Gb };

        db.Tenants.AddRange(tenantA, tenantB);
        db.SubscriptionPlans.Add(plan);
        db.ModuleCatalog.Add(module);
        db.TenantSubscriptions.Add(subscriptionA);
        db.TenantStorageStats.AddRange(statsA, statsB);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task QuotaCheck_AllowsWithinAndRejectsBeyond_RealPostgresRows()
    {
        await using var db = CreateContext();
        var service = CreateService(db);

        var withinLimit = await service.EnsureCanConsumeStorageAsync(_tenantAId, 1 * Gb);
        withinLimit.IsSuccess.Should().BeTrue("99 GB used + 1 GB fits the 100 GB module allowance exactly");

        var overLimit = await service.EnsureCanConsumeStorageAsync(_tenantAId, (1 * Gb) + 1);
        overLimit.IsSuccess.Should().BeFalse();
        overLimit.StatusCode.Should().Be(409);
        overLimit.Error.Should().Be(StorageQuotaErrorCodes.QuotaExceeded);
    }

    [Fact]
    public async Task QuotaCheck_IsTenantIsolated_RealPostgresRows()
    {
        await using var db = CreateContext();
        var service = CreateService(db);

        // Tenant B's 900 GB usage must not affect tenant A.
        var tenantADecision = await service.EnsureCanConsumeStorageAsync(_tenantAId, 1 * Gb);
        tenantADecision.IsSuccess.Should().BeTrue();

        // Tenant B has no subscription and no platform default: not entitled.
        var tenantBDecision = await service.EnsureCanConsumeStorageAsync(_tenantBId, 1);
        tenantBDecision.IsSuccess.Should().BeFalse();
        tenantBDecision.StatusCode.Should().Be(403);
        tenantBDecision.Error.Should().Be(StorageQuotaErrorCodes.NotEntitled);
    }

    [Fact]
    public async Task StatsRow_ForExistingTenant_Inserts()
    {
        await using var db = CreateContext();
        var tenant = NewTenant("Quota FK Tenant", "quota-fk-tenant");
        db.Tenants.Add(tenant);
        db.TenantStorageStats.Add(new TenantStorageStats { TenantId = tenant.Id, UsedR2Bytes = 1 });

        await db.SaveChangesAsync();

        (await db.TenantStorageStats.AsNoTracking().CountAsync(s => s.TenantId == tenant.Id))
            .Should().Be(1);
    }

    [Fact]
    public async Task StatsRow_ForNonExistingTenant_FailsWithForeignKeyViolation()
    {
        await using var db = CreateContext();
        db.TenantStorageStats.Add(new TenantStorageStats { TenantId = Guid.NewGuid(), UsedR2Bytes = 1 });

        var act = async () => await db.SaveChangesAsync();

        var failure = await act.Should().ThrowAsync<DbUpdateException>(
            "tenant_storage_stats.tenant_id must be a foreign key to tenants.id");
        failure.Which.InnerException.Should().BeOfType<Npgsql.PostgresException>()
            .Which.SqlState.Should().Be("23503", "23503 is the PostgreSQL foreign_key_violation code");
    }

    private IStorageQuotaService CreateService(ApplicationDbContext db) =>
        new StorageQuotaService(
            new EfSubscriptionRepository(db),
            new ModuleCatalogRepository(db),
            new EfTenantStorageStatsRepository(db),
            Options.Create(new StorageQuotaOptions { DefaultStorageLimitGb = null }));

    private static Tenant NewTenant(string name, string slug) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Slug = slug,
        CompanySizeRange = "51-200",
        Status = TenantStatus.Active
    };

    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }
}
