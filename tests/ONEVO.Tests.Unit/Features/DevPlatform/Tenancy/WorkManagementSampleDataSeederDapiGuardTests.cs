using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Seeders;
using ONEVO.Tests.Unit.Features.Auth;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Tenancy;

/// <summary>
/// Proves WorkManagementSampleDataSeeder's dapi-tenant guard is tenant-specific, not a global
/// kill-switch: seeds both the dapi smoke tenant (via DevSmokeTestTenantSeeder) and a second,
/// independent active tenant, runs the sample seeder once, and asserts dapi gets zero "SMK..."
/// projects while the other tenant still gets its normal per-user sample projects.
/// </summary>
public sealed class WorkManagementSampleDataSeederDapiGuardTests : IDisposable
{
    private static readonly Guid DapiTenantId = Guid.Parse("6b0874ab-71db-401f-859f-bdd50c1317fb");
    private const string DapiOwnerEmail = "dapiyshanth1908@gmail.com";

    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly TestClock _clock = new();

    public WorkManagementSampleDataSeederDapiGuardTests()
    {
        var databaseName = $"sample_seeder_dapi_guard_tests_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared;Foreign Keys=False";

        _masterConnection = new SqliteConnection(_connectionString);
        _masterConnection.Open();

        using var schemaContext = CreateContext();
        schemaContext.Database.EnsureCreated();
        schemaContext.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS global_email_directory (
                email TEXT NOT NULL,
                tenant_id TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (email, tenant_id)
            );
            """);
    }

    public void Dispose()
    {
        _masterConnection.Dispose();
    }

    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new SqliteTestApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }

    private static async Task SeedPermissionsAsync(ApplicationDbContext db)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => db);
        var sp = services.BuildServiceProvider();
        var seeder = new PermissionSeeder(sp, NullLogger<PermissionSeeder>.Instance);
        var method = typeof(PermissionSeeder).GetMethod(
            "SeedPermissionsAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)method.Invoke(seeder, [db, CancellationToken.None])!;
    }

    private static Mock<IPasswordHasher> CreatePasswordHasher()
    {
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed-password");
        return hasher;
    }

    private static async Task SeedLookupDataAsync(ApplicationDbContext db)
    {
        if (!await db.EmploymentTypes.AnyAsync())
        {
            db.EmploymentTypes.Add(new EmploymentType { Id = 1, Code = "full_time", Label = "Full-Time" });
        }
        if (!await db.EmploymentStatuses.AnyAsync())
        {
            db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        }
        if (!await db.WorkModes.AnyAsync())
        {
            db.WorkModes.Add(new WorkMode { Id = 1, Code = "on_site", Label = "On-Site" });
        }
        await db.SaveChangesAsync();
    }

    private static readonly string[] CanonicalPhase1Modules =
    [
        "org_structure", "core_hr", "leave", "calendar", "time_attendance",
        "monitoring", "discrepancy_engine", "identity_verification",
        "exception_engine", "productivity_analytics", "desktop_agent_gateway",
        "worksync_foundation", "projects", "objectives_milestones", "tasks",
        "boards", "planning_sprints"
    ];

    private static async Task SeedSubscriptionPlanAsync(ApplicationDbContext db)
    {
        if (await db.SubscriptionPlans.AnyAsync(p => p.Code == "starter_51_200"))
        {
            return;
        }

        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = "Starter",
            Code = "starter_51_200",
            Tier = "starter",
            CompanySizeRange = "51-200",
            IncludedModulesJson = System.Text.Json.JsonSerializer.Serialize(CanonicalPhase1Modules)
        });
        await db.SaveChangesAsync();
    }

    private static async Task RunDevSmokeSeederAsync(ApplicationDbContext db)
    {
        await SeedPermissionsAsync(db);
        await SeedLookupDataAsync(db);
        await SeedSubscriptionPlanAsync(db);
        var tenantContext = new TenantContextAccessor();
        await DevSmokeTestTenantSeeder.SeedAsync(
            db,
            tenantContext,
            CreatePasswordHasher().Object,
            new Mock<IEncryptionService>().Object,
            new ConfigurationBuilder().Build(),
            CancellationToken.None);
    }

    [Fact]
    public async Task RunningSampleSeeder_ProducesZeroSmkProjectsForDapiTenant_ButStillSeedsOtherTenants()
    {
        using var db = CreateContext();
        await RunDevSmokeSeederAsync(db);

        var tenantContext = new TenantContextAccessor();
        await WorkManagementSampleDataSeeder.SeedAsync(db, tenantContext, CancellationToken.None);

        using var verify = CreateContext();

        var dapiSmkProjectCount = await verify.Projects
            .Where(p => p.TenantId == DapiTenantId && p.Identifier.StartsWith("SMK"))
            .CountAsync();
        dapiSmkProjectCount.Should().Be(0);

        var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");
        var acmeSmkProjectCount = await verify.Projects
            .Where(p => p.TenantId == acmeTenant.Id && p.Identifier.StartsWith("SMK"))
            .CountAsync();
        acmeSmkProjectCount.Should().BeGreaterThan(0); // proves the guard is dapi-specific, not global
    }

    [Fact]
    public async Task SeedAsync_EveryObjectiveOwnerId_HasMatchingEmployeeRecord()
    {
        // Arrange - seed via the real seeder, exactly as production startup does.
        using var db = CreateContext();
        await RunDevSmokeSeederAsync(db);

        var tenantContext = new TenantContextAccessor();
        await WorkManagementSampleDataSeeder.SeedAsync(db, tenantContext, CancellationToken.None);

        using var verify = CreateContext();

        // Act
        var objectives = await verify.Objectives.AsNoTracking().ToListAsync();
        var employeeIds = await verify.Employees.AsNoTracking().Select(e => e.Id).ToListAsync();

        // Assert - every seeded OwnerId must be a real Employee.Id, never a bare UserId.
        objectives.Should().NotBeEmpty();
        Assert.All(objectives, o => Assert.Contains(o.OwnerId, employeeIds));
    }

    private sealed class TestClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }
}
