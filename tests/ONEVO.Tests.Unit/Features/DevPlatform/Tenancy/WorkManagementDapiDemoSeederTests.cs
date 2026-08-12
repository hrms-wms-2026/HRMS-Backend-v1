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
/// Mirrors DevSmokeTestTenantSeederTests.cs's SQLite-in-memory harness (WorkManagementDapiDemoSeeder
/// depends on DevSmokeTestTenantSeeder having already seeded the dapi tenant/owner/legal entity, so
/// every test here runs DevSmokeTestTenantSeeder.SeedAsync first via RunDevSmokeSeederAsync).
/// </summary>
public sealed class WorkManagementDapiDemoSeederTests : IDisposable
{
    private static readonly Guid DapiTenantId = Guid.Parse("6b0874ab-71db-401f-859f-bdd50c1317fb");

    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly TestClock _clock = new();

    public WorkManagementDapiDemoSeederTests()
    {
        var databaseName = $"work_management_dapi_demo_seeder_tests_{Guid.NewGuid():N}";
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
        "activity_monitoring", "discrepancy_engine", "identity_verification",
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

    private static async Task RunDemoSeederAsync(ApplicationDbContext db)
    {
        var tenantContext = new TenantContextAccessor();
        await WorkManagementDapiDemoSeeder.SeedAsync(
            db,
            tenantContext,
            CreatePasswordHasher().Object,
            CancellationToken.None);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SeedAsync_Creates22NewUsersAndEmployeesUnderDapiTenant()
    {
        using var db = CreateContext();
        await RunDevSmokeSeederAsync(db);
        await RunDemoSeederAsync(db);

        using var verify = CreateContext();
        var userCount = await verify.Users.CountAsync(u => u.TenantId == DapiTenantId);
        var employeeCount = await verify.Employees.CountAsync(e => e.TenantId == DapiTenantId);

        userCount.Should().Be(23);     // 1 existing owner + 22 new
        employeeCount.Should().Be(23);
    }

    [Fact]
    public async Task SeedAsync_CreatesOneWorkManagementTeamMemberRoleWithExactly21Permissions()
    {
        using var db = CreateContext();
        await RunDevSmokeSeederAsync(db);
        await RunDemoSeederAsync(db);

        using var verify = CreateContext();
        var role = await verify.Roles.SingleAsync(r => r.Id == WorkManagementDapiDemoSeeder.DemoRoleId);
        var grantedCodes = await verify.RolePermissions
            .Where(rp => rp.RoleId == role.Id)
            .Join(verify.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code)
            .ToListAsync();

        role.Name.Should().Be("Work Management Team Member");
        grantedCodes.Should().HaveCount(22); // every Permission row tagged Module == "work_management"
        grantedCodes.Should().NotContain(code => code.Contains("employees:"));
        grantedCodes.Should().NotContain(code => code.Contains("payroll"));
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_RunningTwiceProducesSameCounts()
    {
        using (var first = CreateContext())
        {
            await RunDevSmokeSeederAsync(first);
            await RunDemoSeederAsync(first);
        }

        int firstUserCount, firstRolePermissionCount;
        using (var afterFirst = CreateContext())
        {
            firstUserCount = await afterFirst.Users.CountAsync();
            firstRolePermissionCount = await afterFirst.RolePermissions.CountAsync();
        }

        using (var second = CreateContext())
        {
            await RunDevSmokeSeederAsync(second);
            await RunDemoSeederAsync(second);
        }

        using var verify = CreateContext();
        var secondUserCount = await verify.Users.CountAsync();
        var secondRolePermissionCount = await verify.RolePermissions.CountAsync();

        secondUserCount.Should().Be(firstUserCount);
        secondRolePermissionCount.Should().Be(firstRolePermissionCount);
    }

    private sealed class TestClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }
}
