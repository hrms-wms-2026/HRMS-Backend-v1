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

    // Mirrors the canonical Phase 1 product module list seeded onto the "starter_51_200"
    // plan by migration 20260804024502_UpdateStarterPlanToCanonicalPhase1Modules.
    private static readonly string[] CanonicalPhase1Modules =
    [
        "org_structure", "core_hr", "leave", "calendar", "time_attendance",
        "activity_monitoring", "discrepancy_engine", "identity_verification",
        "exception_engine", "productivity_analytics", "desktop_agent_gateway",
        "worksync_foundation", "projects", "objectives_milestones", "tasks",
        "boards", "planning_sprints"
    ];

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

    private static async Task RunSeederAsync(ApplicationDbContext db)
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
    public async Task SeedAsync_CreatesExactlyOneEmployeeRowPerSeededUser()
    {
        using (var first = CreateContext())
        {
            await RunSeederAsync(first);
        }

        using var verify = CreateContext();
        var seededUserIds = await verify.Users
            .Where(u => u.Email == AcmeOwnerEmail || u.Email == AcmeHrManagerEmail ||
                        u.Email == AcmeWorkManagerEmail || u.Email == DapiOwnerEmail)
            .Select(u => u.Id)
            .ToListAsync();

        var employees = await verify.Set<Employee>()
            .Where(e => seededUserIds.Contains(e.UserId))
            .ToListAsync();

        employees.Should().HaveCount(4);
        employees.Select(e => e.UserId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task SeedAsync_AcmeOwnerGetsAcmeTechnologiesEmployeeNumberAcme0001()
    {
        using var db = CreateContext();
        await RunSeederAsync(db);

        using var verify = CreateContext();
        var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");
        var technologies = await verify.LegalEntities.SingleAsync(l => l.Name == "Acme Technologies");
        var owner = await verify.Users.SingleAsync(u => u.Email == AcmeOwnerEmail);
        var employee = await verify.Set<Employee>().SingleAsync(e => e.UserId == owner.Id);

        employee.TenantId.Should().Be(acmeTenant.Id);
        employee.LegalEntityId.Should().Be(technologies.Id);
        employee.EmployeeNumber.Should().Be("ACME-0001");
        employee.FirstName.Should().Be(owner.FirstName);
        employee.LastName.Should().Be(owner.LastName);
        employee.Email.Should().Be(owner.Email);
    }

    [Fact]
    public async Task SeedAsync_AcmeHrManagerGetsAcmeTechnologiesEmployeeNumberAcme0002()
    {
        using var db = CreateContext();
        await RunSeederAsync(db);

        using var verify = CreateContext();
        var technologies = await verify.LegalEntities.SingleAsync(l => l.Name == "Acme Technologies");
        var user = await verify.Users.SingleAsync(u => u.Email == AcmeHrManagerEmail);
        var employee = await verify.Set<Employee>().SingleAsync(e => e.UserId == user.Id);

        employee.LegalEntityId.Should().Be(technologies.Id);
        employee.EmployeeNumber.Should().Be("ACME-0002");
    }

    [Fact]
    public async Task SeedAsync_AcmeWorkManagerHasExactlyOneEmployeeRowUnderAcmeSolutions()
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
        var solutions = await verify.LegalEntities.SingleAsync(l => l.Name == "Acme Solutions");
        var user = await verify.Users.SingleAsync(u => u.Email == AcmeWorkManagerEmail);
        var employees = await verify.Set<Employee>().Where(e => e.UserId == user.Id).ToListAsync();

        employees.Should().ContainSingle();
        employees[0].LegalEntityId.Should().Be(solutions.Id);
        employees[0].EmployeeNumber.Should().Be("ACME-0003");
    }

    [Fact]
    public async Task SeedAsync_DapiOwnerGetsDapiLegalEntityEmployeeNumberDapi0001()
    {
        using var db = CreateContext();
        await RunSeederAsync(db);

        using var verify = CreateContext();
        var dapiTenant = await verify.Tenants.SingleAsync(t => t.Slug == "dapi");
        var dapiLegalEntity = await verify.LegalEntities.SingleAsync(l => l.TenantId == dapiTenant.Id);
        var owner = await verify.Users.SingleAsync(u => u.Email == DapiOwnerEmail);
        var employee = await verify.Set<Employee>().SingleAsync(e => e.UserId == owner.Id);

        employee.TenantId.Should().Be(dapiTenant.Id);
        employee.LegalEntityId.Should().Be(dapiLegalEntity.Id);
        employee.EmployeeNumber.Should().Be("DAPI-0001");
    }

    [Fact]
    public async Task SeedAsync_NoEmployeeRowIsCreatedForAUserInTheWrongTenant()
    {
        using var db = CreateContext();
        await RunSeederAsync(db);

        using var verify = CreateContext();
        var dapiTenant = await verify.Tenants.SingleAsync(t => t.Slug == "dapi");
        var acmeOwner = await verify.Users.SingleAsync(u => u.Email == AcmeOwnerEmail);
        var employee = await verify.Set<Employee>().SingleAsync(e => e.UserId == acmeOwner.Id);

        employee.TenantId.Should().NotBe(dapiTenant.Id);
    }

    [Fact]
    public async Task SeedAsync_IsIdempotentAcrossRepeatedRunsForEmployees()
    {
        using (var first = CreateContext())
        {
            await RunSeederAsync(first);
        }
        using (var second = CreateContext())
        {
            await RunSeederAsync(second);
        }
        using (var third = CreateContext())
        {
            await RunSeederAsync(third);
        }

        using var verify = CreateContext();
        (await verify.Set<Employee>().CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task SeedAsync_EmployeeNumbersAreUniquePerTenant()
    {
        using var db = CreateContext();
        await RunSeederAsync(db);

        using var verify = CreateContext();
        var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");
        var numbers = await verify.Set<Employee>()
            .Where(e => e.TenantId == acmeTenant.Id)
            .Select(e => e.EmployeeNumber)
            .ToListAsync();

        numbers.Should().OnlyHaveUniqueItems();
        numbers.Should().BeEquivalentTo(["ACME-0001", "ACME-0002", "ACME-0003"]);
    }

    [Fact]
    public async Task SeedAsync_LeavesDepartmentIdNullForEverySmokeEmployee()
    {
        using var db = CreateContext();
        await RunSeederAsync(db);

        using var verify = CreateContext();
        var departmentIds = await verify.Set<Employee>().Select(e => e.DepartmentId).ToListAsync();

        departmentIds.Should().AllSatisfy(id => id.Should().BeNull());
    }

    [Fact]
    public async Task SeedAsync_ReusesExistingEmployeeRowInsteadOfDuplicatingOnRerun()
    {
        using (var first = CreateContext())
        {
            await RunSeederAsync(first);
        }

        Guid employeeId;
        using (var mid = CreateContext())
        {
            var owner = await mid.Users.SingleAsync(u => u.Email == AcmeOwnerEmail);
            employeeId = (await mid.Set<Employee>().SingleAsync(e => e.UserId == owner.Id)).Id;
        }

        using (var second = CreateContext())
        {
            await RunSeederAsync(second);
        }

        using var verify = CreateContext();
        var ownerAfter = await verify.Users.SingleAsync(u => u.Email == AcmeOwnerEmail);
        var employeeAfter = await verify.Set<Employee>().SingleAsync(e => e.UserId == ownerAfter.Id);

        employeeAfter.Id.Should().Be(employeeId);
    }

    [Fact]
    public async Task SeedAsync_ThrowsWhenEmployeeNumberCollidesWithADifferentUserInTheSameTenant()
    {
        using var db = CreateContext();
        await SeedPermissionsAsync(db);
        await SeedLookupDataAsync(db);

        var tenantContext = new TenantContextAccessor();

        // Seed tenants/users/legal entities first via one normal pass so the tenant + user rows
        // exist, then hand-plant a conflicting employee_number under a foreign UserId before the
        // *next* pass tries to seed the real ACME-0001 row for the real owner - this simulates a
        // dirty dev database rather than a fresh one.
        await DevSmokeTestTenantSeeder.SeedAsync(
            db, tenantContext, CreatePasswordHasher().Object,
            new Mock<IEncryptionService>().Object, new ConfigurationBuilder().Build(), CancellationToken.None);

        var owner = await db.Users.SingleAsync(u => u.Email == AcmeOwnerEmail);
        var conflictingEmployee = await db.Set<Employee>().SingleAsync(e => e.UserId == owner.Id);

        // Repoint the existing ACME-0001 row onto a different, unrelated user id so the next
        // seeder pass sees "employee_number ACME-0001 belongs to someone else."
        conflictingEmployee.UserId = Guid.NewGuid();
        await db.SaveChangesAsync();

        var act = () => DevSmokeTestTenantSeeder.SeedAsync(
            db, tenantContext, CreatePasswordHasher().Object,
            new Mock<IEncryptionService>().Object, new ConfigurationBuilder().Build(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ACME-0001*");
    }

    [Fact]
    public void EmployeeEntity_UserIdIndex_IsUnique()
    {
        using var db = CreateContext();
        var entityType = db.Model.FindEntityType(typeof(Employee))!;
        var index = entityType.GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual(["UserId"]));

        index.IsUnique.Should().BeTrue();
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

    [Fact]
    public async Task SeedAsync_AcmeAndDapiSubscriptionsMatchTheCanonicalPhase1ModuleList()
    {
        using var db = CreateContext();
        await RunSeederAsync(db);

        using var verify = CreateContext();
        var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");
        var dapiTenant = await verify.Tenants.SingleAsync(t => t.Slug == "dapi");
        var acmeSubscription = await verify.TenantSubscriptions.SingleAsync(s => s.TenantId == acmeTenant.Id);
        var dapiSubscription = await verify.TenantSubscriptions.SingleAsync(s => s.TenantId == dapiTenant.Id);

        var acmeModules = System.Text.Json.JsonSerializer.Deserialize<List<string>>(acmeSubscription.SelectedModulesJson);
        var dapiModules = System.Text.Json.JsonSerializer.Deserialize<List<string>>(dapiSubscription.SelectedModulesJson);

        acmeModules.Should().BeEquivalentTo(CanonicalPhase1Modules);
        dapiModules.Should().BeEquivalentTo(CanonicalPhase1Modules);
        acmeModules.Should().Contain("org_structure");
        dapiModules.Should().Contain("org_structure");

        // Platform baseline capabilities must never appear as subscribed product modules.
        acmeModules.Should().NotContain(["auth", "configuration", "roles", "notifications"]);
        dapiModules.Should().NotContain(["auth", "configuration", "roles", "notifications"]);
    }

    [Fact]
    public async Task SeedAsync_CorrectsAStaleSelectedModulesJsonAlreadyPersistedInTheDevDatabase()
    {
        using (var first = CreateContext())
        {
            await RunSeederAsync(first);
        }

        Guid acmeTenantId;
        using (var stale = CreateContext())
        {
            acmeTenantId = (await stale.Tenants.SingleAsync(t => t.Slug == "acme")).Id;
            var subscription = await stale.TenantSubscriptions.SingleAsync(s => s.TenantId == acmeTenantId);

            // Simulates a dev database that still holds the pre-fix stale module list, e.g.
            // written by an old seeder build or a manual DB edit that did not persist.
            subscription.SelectedModulesJson = """["integrations","work_management"]""";
            await stale.SaveChangesAsync();
        }

        using (var second = CreateContext())
        {
            await RunSeederAsync(second);
        }

        using var verify = CreateContext();
        var subscriptionAfter = await verify.TenantSubscriptions.SingleAsync(s => s.TenantId == acmeTenantId);
        var modulesAfter = System.Text.Json.JsonSerializer.Deserialize<List<string>>(subscriptionAfter.SelectedModulesJson);

        modulesAfter.Should().BeEquivalentTo(CanonicalPhase1Modules);
        modulesAfter.Should().Contain("org_structure");
        modulesAfter.Should().NotContain("integrations");
        modulesAfter.Should().NotContain("work_management");
    }

    [Fact]
    public async Task SeedAsync_AcmeOwnerHasOrgReadAndOrgManagePermissionCodes()
    {
        using var db = CreateContext();
        await RunSeederAsync(db);

        using var verify = CreateContext();
        var acmeTenant = await verify.Tenants.SingleAsync(t => t.Slug == "acme");
        var owner = await verify.Users.SingleAsync(u => u.Email == AcmeOwnerEmail);
        var ownerCodes = await RolePermissionCodesForAsync(verify, acmeTenant.Id, owner.Id);

        ownerCodes.Should().Contain("org:read");
        ownerCodes.Should().Contain("org:manage");
    }

    [Fact]
    public async Task SeedAsync_DoesNotDuplicateTenantSubscriptionsAcrossRepeatedRuns()
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
        var dapiTenant = await verify.Tenants.SingleAsync(t => t.Slug == "dapi");

        (await verify.TenantSubscriptions.CountAsync(s => s.TenantId == acmeTenant.Id)).Should().Be(1);
        (await verify.TenantSubscriptions.CountAsync(s => s.TenantId == dapiTenant.Id)).Should().Be(1);
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
