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
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Seeders;
using ONEVO.Tests.Unit.Features.Auth;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Tenancy;

/// <summary>
/// Runs DevSmokeTestTenantSeeder.SeedAsync against SQLite in-memory (via the test-only
/// SqliteTestApplicationDbContext) rather than the EF InMemory provider, because the seeder's
/// global_email_directory writes use ExecuteSqlInterpolatedAsync, which InMemory cannot execute.
/// global_email_directory has no EF entity mapping in production (it only exists via a raw-SQL
/// migration), so it is recreated here with equivalent DDL, test-only, after EnsureCreated().
/// </summary>
public sealed class DevSmokeTestTenantSeederTests : IDisposable
{
    private const string AcmeOwnerEmail = "siyasiyamala932@gmail.com";
    private const string AcmeHrManagerEmail = "paramanathanmuthaiya@gmail.com";
    private const string AcmeWorkManagerEmail = "mrt15473@gmail.com";
    private const string DapiOwnerEmail = "dapiyshanth1908@gmail.com";

    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly TestClock _clock = new();

    public DevSmokeTestTenantSeederTests()
    {
        var databaseName = $"dev_smoke_seed_tests_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared;Foreign Keys=False";

        // A named shared in-memory SQLite database lives only while at least one connection to
        // it stays open; this master connection pins it for the duration of the test.
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

    private static async Task RunSeederAsync(ApplicationDbContext db)
    {
        await SeedPermissionsAsync(db);
        var tenantContext = new TenantContextAccessor();
        await DevSmokeTestTenantSeeder.SeedAsync(
            db,
            tenantContext,
            CreatePasswordHasher().Object,
            new Mock<IEncryptionService>().Object,
            new ConfigurationBuilder().Build(),
            CancellationToken.None);
    }

    private static async Task<HashSet<string>> RolePermissionCodesForAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid userId)
    {
        var codes = await (
            from ur in db.UserRoles
            join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.TenantId == tenantId && ur.UserId == userId
            select p.Code).ToListAsync();
        return codes.ToHashSet();
    }

    [Fact]
    public async Task SeedAsync_CreatesBothAcmeAndDapiTenants()
    {
        using var db = CreateContext();
        await RunSeederAsync(db);

        using var verify = CreateContext();
        var slugs = await verify.Tenants.Select(t => t.Slug).ToListAsync();

        slugs.Should().Contain(["acme", "dapi"]);
    }

    [Fact]
    public async Task SeedAsync_DapiOwnerBelongsOnlyToDapi()
    {
        using var db = CreateContext();
        await RunSeederAsync(db);

        using var verify = CreateContext();
        var dapiTenant = await verify.Tenants.SingleAsync(t => t.Slug == "dapi");
        var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");
        var dapiOwner = await verify.Users.SingleAsync(u => u.Email == DapiOwnerEmail);

        dapiOwner.TenantId.Should().Be(dapiTenant.Id);
        (await verify.Users.AnyAsync(u => u.Email == DapiOwnerEmail && u.TenantId == acmeTenant.Id))
            .Should().BeFalse();
    }

    [Fact]
    public async Task SeedAsync_AcmeOwnerBelongsToAcmeWithFullPermissionsExceptWildcard()
    {
        using var db = CreateContext();
        await RunSeederAsync(db);

        using var verify = CreateContext();
        var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");
        var owner = await verify.Users.SingleAsync(u => u.Email == AcmeOwnerEmail);
        var allPermissionCount = await verify.Permissions.CountAsync(p => p.Code != "*");

        owner.TenantId.Should().Be(acmeTenant.Id);
        var ownerCodes = await RolePermissionCodesForAsync(verify, acmeTenant.Id, owner.Id);

        ownerCodes.Should().HaveCount(allPermissionCount);
        ownerCodes.Should().NotContain("*");
    }

    [Fact]
    public async Task SeedAsync_AcmeHrManagerBelongsToAcmeWithItsRequiredPermissions()
    {
        using var db = CreateContext();
        await RunSeederAsync(db);

        using var verify = CreateContext();
        var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");
        var user = await verify.Users.SingleAsync(u => u.Email == AcmeHrManagerEmail);

        user.TenantId.Should().Be(acmeTenant.Id);
        var codes = await RolePermissionCodesForAsync(verify, acmeTenant.Id, user.Id);

        codes.Should().BeEquivalentTo(
            ["org:read", "org:manage", "employees:read", "employees:write", "roles:read"]);
    }

    [Fact]
    public async Task SeedAsync_AcmeWorkManagerBelongsToAcmeWithItsRequiredPermissions()
    {
        using var db = CreateContext();
        await RunSeederAsync(db);

        using var verify = CreateContext();
        var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");
        var user = await verify.Users.SingleAsync(u => u.Email == AcmeWorkManagerEmail);

        user.TenantId.Should().Be(acmeTenant.Id);
        var codes = await RolePermissionCodesForAsync(verify, acmeTenant.Id, user.Id);

        codes.Should().BeEquivalentTo(
            ["org:read", "employees:read", "projects:read", "tasks:read", "tasks:write"]);
        codes.Should().NotContain("org:manage");
    }

    [Fact]
    public async Task SeedAsync_TheThreeAcmeUsersHaveDifferentPermissionSets()
    {
        using var db = CreateContext();
        await RunSeederAsync(db);

        using var verify = CreateContext();
        var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");

        async Task<HashSet<string>> CodesFor(string email)
        {
            var user = await verify.Users.SingleAsync(u => u.Email == email);
            return await RolePermissionCodesForAsync(verify, acmeTenant.Id, user.Id);
        }

        var owner = await CodesFor(AcmeOwnerEmail);
        var hrManager = await CodesFor(AcmeHrManagerEmail);
        var workManager = await CodesFor(AcmeWorkManagerEmail);

        owner.Should().NotBeEquivalentTo(hrManager);
        owner.Should().NotBeEquivalentTo(workManager);
        hrManager.Should().NotBeEquivalentTo(workManager);
    }

    [Fact]
    public async Task SeedAsync_AcmeHasExactlyThreeLegalEntitiesAfterRepeatedSeeding()
    {
        using (var first = CreateContext())
        {
            await RunSeederAsync(first);
        }
        using (var second = CreateContext())
        {
            await RunSeederAsync(second);
        }

        using var verify = CreateContext();
        var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");
        var legalEntities = await verify.LegalEntities.Where(l => l.TenantId == acmeTenant.Id).ToListAsync();

        legalEntities.Should().HaveCount(3);
        legalEntities.Select(l => l.Name).Should().BeEquivalentTo(
            ["Acme Technologies", "Acme Solutions", "Acme Global Services"]);
        legalEntities.Count(l => l.IsPrimary).Should().Be(1);
        legalEntities.Single(l => l.IsPrimary).Name.Should().Be("Acme Technologies");
    }

    [Fact]
    public async Task SeedAsync_DapiHasExactlyOneLegalEntityAfterRepeatedSeeding()
    {
        using (var first = CreateContext())
        {
            await RunSeederAsync(first);
        }
        using (var second = CreateContext())
        {
            await RunSeederAsync(second);
        }

        using var verify = CreateContext();
        var dapiTenant = await verify.Tenants.SingleAsync(t => t.Slug == "dapi");
        var legalEntities = await verify.LegalEntities.Where(l => l.TenantId == dapiTenant.Id).ToListAsync();

        legalEntities.Should().ContainSingle();
        legalEntities[0].Name.Should().Be("Dapi Technologies");
        legalEntities[0].IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task SeedAsync_IsIdempotentAcrossTenantsUsersAndRoles()
    {
        using (var first = CreateContext())
        {
            await RunSeederAsync(first);
        }
        using (var second = CreateContext())
        {
            await RunSeederAsync(second);
        }

        using var verify = CreateContext();
        (await verify.Tenants.CountAsync(t => t.Slug == "acme" || t.Slug == "dapi")).Should().Be(2);
        (await verify.Users.CountAsync(u =>
            u.Email == AcmeOwnerEmail || u.Email == AcmeHrManagerEmail ||
            u.Email == AcmeWorkManagerEmail || u.Email == DapiOwnerEmail)).Should().Be(4);
        (await verify.Roles.CountAsync(r => r.Name == "Tenant Owner")).Should().Be(2);
        (await verify.Roles.CountAsync(r => r.Name == "HR Manager")).Should().Be(1);
        (await verify.Roles.CountAsync(r => r.Name == "Work Manager")).Should().Be(1);
    }

    [Fact]
    public async Task SeedAsync_DoesNotCreateAnyEmployeeRowForWorkManager()
    {
        using (var first = CreateContext())
        {
            await RunSeederAsync(first);
        }
        using (var second = CreateContext())
        {
            await RunSeederAsync(second);
        }

        using var verify = CreateContext();
        var user = await verify.Users.SingleAsync(u => u.Email == AcmeWorkManagerEmail);

        (await verify.Set<Employee>().AnyAsync(e => e.UserId == user.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task SeedAsync_AllSeededRolePermissionAndUserRoleRowsHaveNonEmptyTenantId()
    {
        using var db = CreateContext();
        await RunSeederAsync(db);

        using var verify = CreateContext();
        (await verify.RolePermissions.AnyAsync(rp => rp.TenantId == Guid.Empty)).Should().BeFalse();
        (await verify.UserRoles.AnyAsync(ur => ur.TenantId == Guid.Empty)).Should().BeFalse();
    }

    [Fact]
    public async Task SeedAsync_EverySeededUserHasAGlobalEmailDirectoryRowForItsTenant()
    {
        using var db = CreateContext();
        await RunSeederAsync(db);

        using var verify = CreateContext();
        var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");
        var dapiTenant = await verify.Tenants.SingleAsync(t => t.Slug == "dapi");

        // Bind the raw Guid (not .ToString()) so Microsoft.Data.Sqlite encodes it the same way
        // as the seeder's ExecuteSqlInterpolatedAsync insert did - Sqlite stores a Guid
        // parameter as a 16-byte BLOB regardless of the column's declared TEXT affinity, so a
        // string-encoded parameter here would never match the inserted rows.
        var acmeRows = await verify.Database
            .SqlQueryRaw<string>(
                "SELECT email FROM global_email_directory WHERE tenant_id = {0}",
                acmeTenant.Id)
            .ToListAsync();
        var dapiRows = await verify.Database
            .SqlQueryRaw<string>(
                "SELECT email FROM global_email_directory WHERE tenant_id = {0}",
                dapiTenant.Id)
            .ToListAsync();

        acmeRows.Should().BeEquivalentTo([AcmeOwnerEmail, AcmeHrManagerEmail, AcmeWorkManagerEmail]);
        dapiRows.Should().BeEquivalentTo([DapiOwnerEmail]);
    }

    [Fact]
    public async Task SeedAsync_ScopedCleanupRemovesOwnStaleRowButNeverTouchesOtherTenants()
    {
        using (var first = CreateContext())
        {
            await RunSeederAsync(first);
        }

        var otherTenantId = Guid.NewGuid();
        Guid acmeTenantId;
        using (var stale = CreateContext())
        {
            acmeTenantId = (await stale.Tenants.SingleAsync(t => t.Slug == "acme")).Id;

            // A stale row left under Acme's own tenant from a retired seeder-version email -
            // the scoped DELETE should remove it because it is not part of Acme's current
            // seeded email set.
            await stale.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO global_email_directory (email, tenant_id) VALUES ({"retired@acme.test"}, {acmeTenantId})");

            // A row under a tenant the seeder never visits at all - proves the per-tenant scope
            // never reaches across tenant boundaries, regardless of what it deletes for its own
            // tenant.
            await stale.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO global_email_directory (email, tenant_id) VALUES ({"unrelated@other.test"}, {otherTenantId})");
        }

        using (var second = CreateContext())
        {
            await RunSeederAsync(second);
        }

        using var verify = CreateContext();
        var acmeRows = await verify.Database
            .SqlQueryRaw<string>("SELECT email FROM global_email_directory WHERE tenant_id = {0}", acmeTenantId)
            .ToListAsync();
        var otherRows = await verify.Database
            .SqlQueryRaw<string>("SELECT email FROM global_email_directory WHERE tenant_id = {0}", otherTenantId)
            .ToListAsync();

        acmeRows.Should().NotContain("retired@acme.test");
        acmeRows.Should().BeEquivalentTo([AcmeOwnerEmail, AcmeHrManagerEmail, AcmeWorkManagerEmail]);
        otherRows.Should().BeEquivalentTo(["unrelated@other.test"]);
    }

    // Requirement #14 ("no test depends on tenant-host password login"): every test above
    // asserts directly against ApplicationDbContext rows and never exercises a login
    // endpoint/handler, so this requirement is satisfied structurally rather than by a
    // dedicated test.

    private sealed class TestClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }
}
