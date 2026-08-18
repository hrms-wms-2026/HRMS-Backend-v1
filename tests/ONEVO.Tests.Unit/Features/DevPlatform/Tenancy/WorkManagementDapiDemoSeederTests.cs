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
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
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
    private static readonly Guid DapiOwnerUserId = Guid.Parse("cd49a0c2-e978-4055-b8be-7d46a3727e94");

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

    [Fact]
    public async Task SeedAsync_WhenCategoryNameAlreadyExistsWithDifferentId_DoesNotThrowAndReusesExistingRow()
    {
        using var db = CreateContext();
        await RunDevSmokeSeederAsync(db);

        var preexistingId = Guid.NewGuid();
        db.ProjectCategories.Add(new ProjectCategory
        {
            Id = preexistingId,
            TenantId = DapiTenantId,
            Name = "Engineering",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var act = async () => await RunDemoSeederAsync(db);
        await act.Should().NotThrowAsync();

        using var verify = CreateContext();
        var engineering = await verify.ProjectCategories
            .Where(c => c.TenantId == DapiTenantId && c.Name == "Engineering")
            .ToListAsync();
        engineering.Should().HaveCount(1);
        engineering[0].Id.Should().Be(preexistingId);

        var epos = await verify.Projects.SingleAsync(p => p.Identifier == "EPOS");
        epos.CategoryId.Should().Be(preexistingId);
    }

    [Fact]
    public async Task SeedAsync_Creates5ProjectsWithCorrectIdentifiersAndLead()
    {
        using var db = CreateContext();
        await RunDevSmokeSeederAsync(db);
        await RunDemoSeederAsync(db);

        using var verify = CreateContext();
        var projects = await verify.Projects
            .Where(p => p.TenantId == DapiTenantId)
            .ToListAsync();

        projects.Should().HaveCount(5);
        projects.Select(p => p.Identifier).Should().BeEquivalentTo(
            ["EPOS", "EVTIX", "ONEXSO", "WCRAFT", "HWPORTAL"]);

        // Project.LeadId is Employee-typed (Phase 2, 2026-08-14) - resolve "dabi"'s actual
        // Employee.Id from the seeded data rather than comparing against the raw DapiOwnerUserId.
        var dabiEmployee = await verify.Employees.SingleAsync(e => e.UserId == DapiOwnerUserId);
        projects.Should().OnlyContain(p => p.LeadId == dabiEmployee.Id);
    }

    [Fact]
    public async Task SeedAsync_EposProjectTree_MatchesSpecifiedShapeAndOwners()
    {
        using var db = CreateContext();
        await RunDevSmokeSeederAsync(db);
        await RunDemoSeederAsync(db);

        using var verify = CreateContext();
        var project = await verify.Projects.SingleAsync(p => p.Identifier == "EPOS");
        var objectives = await verify.Objectives.Where(o => o.ProjectId == project.Id).ToListAsync();

        objectives.Should().HaveCount(15);

        var root = objectives.Single(o => o.IsDefault);
        root.Title.Should().Be("E-pos_System");
        root.ParentObjectiveId.Should().BeNull();

        var posSystem = objectives.Single(o => o.Title == "Pos System");
        posSystem.ParentObjectiveId.Should().Be(root.Id);

        var systemArchitecture = objectives.Single(o => o.Title == "System architecture");
        systemArchitecture.ParentObjectiveId.Should().Be(posSystem.Id);

        var backendArchitecture = objectives.Single(o => o.Title == "Backend architecture");
        backendArchitecture.ParentObjectiveId.Should().Be(systemArchitecture.Id);

        var databaseSchemaDesign = objectives.Single(o => o.Title == "Database schema design");
        databaseSchemaDesign.ParentObjectiveId.Should().Be(backendArchitecture.Id);

        var testingAndDeployment = objectives.Single(o => o.Title == "Testing and deployment");
        var testingMembers = await verify.ProjectMembers
            .Where(pm => pm.ObjectiveId == testingAndDeployment.Id)
            .ToListAsync();
        testingMembers.Should().HaveCount(2); // owner (nevi) + extra member (mathusanth)
    }

    [Fact]
    public async Task SeedAsync_HardwareIntegrationBranch_ExistsInFourProjectsButNotHwPortalItself()
    {
        using var db = CreateContext();
        await RunDevSmokeSeederAsync(db);
        await RunDemoSeederAsync(db);

        using var verify = CreateContext();
        var projectsWithHardwareBranch = await verify.Objectives
            .Where(o => o.TenantId == DapiTenantId && o.Title == "Hardware Integration")
            .Join(verify.Projects, o => o.ProjectId, p => p.Id, (o, p) => p.Identifier)
            .ToListAsync();

        projectsWithHardwareBranch.Should().BeEquivalentTo(["EPOS", "EVTIX", "ONEXSO", "WCRAFT"]);
    }

    [Fact]
    public async Task SeedAsync_MarketingBranch_ExistsInAllFiveProjects()
    {
        using var db = CreateContext();
        await RunDevSmokeSeederAsync(db);
        await RunDemoSeederAsync(db);

        using var verify = CreateContext();
        var projectsWithMarketingBranch = await verify.Objectives
            .Where(o => o.TenantId == DapiTenantId && o.Title == "Marketing")
            .Join(verify.Projects, o => o.ProjectId, p => p.Id, (o, p) => p.Identifier)
            .ToListAsync();

        projectsWithMarketingBranch.Should().BeEquivalentTo(["EPOS", "EVTIX", "ONEXSO", "WCRAFT", "HWPORTAL"]);
    }

    [Fact]
    public async Task SeedAsync_EveryObjective_SatisfiesParentDateAndHoursContainment()
    {
        using var db = CreateContext();
        await RunDevSmokeSeederAsync(db);
        await RunDemoSeederAsync(db);

        using var verify = CreateContext();
        var allObjectives = await verify.Objectives
            .Where(o => o.TenantId == DapiTenantId)
            .ToListAsync();
        var byId = allObjectives.ToDictionary(o => o.Id);

        foreach (var objective in allObjectives.Where(o => o.ParentObjectiveId.HasValue))
        {
            var parent = byId[objective.ParentObjectiveId!.Value];
            (objective.StartDate >= parent.StartDate).Should().BeTrue(
                $"{objective.Title} starts before parent {parent.Title}");
            (objective.EndDate <= parent.EndDate).Should().BeTrue(
                $"{objective.Title} ends after parent {parent.Title}");
            (objective.AllocatedHours <= parent.AllocatedHours).Should().BeTrue(
                $"{objective.Title} ({objective.AllocatedHours}h) exceeds parent {parent.Title} ({parent.AllocatedHours}h)");
        }
    }

    [Fact]
    public async Task SeedAsync_EveryObjective_ChildrenNeverCollectivelyExceedParentAllocation()
    {
        using var db = CreateContext();
        await RunDevSmokeSeederAsync(db);
        await RunDemoSeederAsync(db);

        using var verify = CreateContext();
        var all = await verify.Objectives
            .Where(o => o.TenantId == DapiTenantId && o.IsActive)
            .ToListAsync();
        var byParent = all.Where(o => o.ParentObjectiveId.HasValue)
            .GroupBy(o => o.ParentObjectiveId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.AllocatedHours));
        var byId = all.ToDictionary(o => o.Id);

        foreach (var (parentId, childSum) in byParent)
        {
            var parent = byId[parentId];
            Assert.True(childSum <= parent.AllocatedHours,
                $"{parent.Title}: children sum to {childSum}h but parent only has {parent.AllocatedHours}h");
        }
    }

    [Theory]
    [InlineData("epos", "E-pos_System", 40)]
    [InlineData("evtix", "Event management ticketing", 30)]
    [InlineData("onexso", "Onexso - HR and Work Management System", 50)]
    [InlineData("watercraft", "Watercraft", 45)]
    [InlineData("hwportal", "The Hardware integration portal", 35)]
    public async Task SeedAsync_ProjectRoot_RetainsEnoughSlackForItsAllocationExtendDemo(
        string projectKey, string rootTitle, decimal requiredSlack)
    {
        _ = projectKey;
        using var db = CreateContext();
        await RunDevSmokeSeederAsync(db);
        await RunDemoSeederAsync(db);

        using var verify = CreateContext();
        var root = await verify.Objectives.SingleAsync(o => o.Title == rootTitle && o.IsDefault);
        var childSum = await verify.Objectives
            .Where(o => o.ParentObjectiveId == root.Id && o.IsActive)
            .SumAsync(o => o.AllocatedHours);

        Assert.True(root.AllocatedHours - childSum >= requiredSlack,
            $"{rootTitle}: only {root.AllocatedHours - childSum}h slack, needs >= {requiredSlack}h for its AllocationExtends demo");
    }

    [Fact]
    public async Task SeedAsync_TotalObjectiveCountAcrossAllFiveProjects_Is66()
    {
        using var db = CreateContext();
        await RunDevSmokeSeederAsync(db);
        await RunDemoSeederAsync(db);

        using var verify = CreateContext();
        var total = await verify.Objectives.CountAsync(o => o.TenantId == DapiTenantId);

        total.Should().Be(66);
    }

    [Fact]
    public async Task SeedAsync_CreatesProjectAndLeafTaskStatusesForEveryLeafWithTasks()
    {
        using var db = CreateContext();
        await RunDevSmokeSeederAsync(db);
        await RunDemoSeederAsync(db);

        using var verify = CreateContext();
        var projects = await verify.Projects.Where(p => p.TenantId == DapiTenantId).ToListAsync();
        projects.Should().HaveCount(5);

        foreach (var project in projects)
        {
            var templates = await verify.TaskStatuses
                .Where(s => s.ProjectId == project.Id && s.ObjectiveId == null)
                .ToListAsync();
            templates.Should().HaveCount(4);
            templates.Select(s => s.Name).Should().BeEquivalentTo(["To Do", "In Process", "Review", "Done"]);
            templates.Single(s => s.Name == "Done").MarksTaskComplete.Should().BeTrue();
        }

        var leafIds = await GetLeafObjectiveIdsAsync(verify);
        foreach (var leafId in leafIds)
        {
            var statuses = await verify.TaskStatuses
                .Where(s => s.ObjectiveId == leafId)
                .ToListAsync();
            statuses.Should().HaveCount(4);
        }
    }

    [Fact]
    public async Task SeedAsync_CreatesTwoToThreeTasksPerLeaf_WithShortIdsAndSlack()
    {
        using var db = CreateContext();
        await RunDevSmokeSeederAsync(db);
        await RunDemoSeederAsync(db);

        using var verify = CreateContext();
        var projects = await verify.Projects.Where(p => p.TenantId == DapiTenantId).ToListAsync();
        var leafIds = await GetLeafObjectiveIdsAsync(verify);

        foreach (var leafId in leafIds)
        {
            var leaf = await verify.Objectives.SingleAsync(o => o.Id == leafId);
            var project = projects.Single(p => p.Id == leaf.ProjectId);
            var tasks = await verify.WorkTasks.Where(t => t.ObjectiveId == leafId).ToListAsync();

            tasks.Should().HaveCount(3);
            tasks.Should().OnlyContain(t => t.ShortId.StartsWith(project.Identifier + "-"));
            tasks.Sum(t => t.EstimatedHours ?? 0m).Should().BeLessThanOrEqualTo(leaf.AllocatedHours);
        }

        foreach (var project in projects)
        {
            var numbers = await verify.WorkTasks
                .Where(t => t.ProjectId == project.Id)
                .Select(t => t.ShortId)
                .ToListAsync();
            var maxNumber = numbers
                .Select(s => long.Parse(s[(project.Identifier.Length + 1)..]))
                .DefaultIfEmpty(0)
                .Max();
            project.NextTaskNumber.Should().Be(maxNumber + 1);
        }
    }

    [Fact]
    public async Task SeedAsync_CreatesPendingApprovalsQueuePerProject()
    {
        using var db = CreateContext();
        await RunDevSmokeSeederAsync(db);
        await RunDemoSeederAsync(db);

        using var verify = CreateContext();
        var projects = await verify.Projects.Where(p => p.TenantId == DapiTenantId).ToListAsync();

        foreach (var project in projects)
        {
            var objectiveIds = await verify.Objectives
                .Where(o => o.ProjectId == project.Id)
                .Select(o => o.Id)
                .ToListAsync();

            var pendingCreates = await verify.TaskCreationRequests
                .CountAsync(r => objectiveIds.Contains(r.ObjectiveId) && r.Status == "pending");
            pendingCreates.Should().BeGreaterThanOrEqualTo(3, because: $"{project.Identifier} needs ≥3 pending task creation requests");

            var pendingExtends = await verify.ObjectiveChangeRequests
                .CountAsync(r =>
                    objectiveIds.Contains(r.ObjectiveId)
                    && r.RequestType == "extend_allocation"
                    && r.Status == "pending");
            pendingExtends.Should().BeGreaterThanOrEqualTo(1, because: $"{project.Identifier} needs ≥1 pending extend_allocation");
        }
    }

    [Fact]
    public async Task SeedAsync_TaskLayer_IsIdempotent()
    {
        using (var first = CreateContext())
        {
            await RunDevSmokeSeederAsync(first);
            await RunDemoSeederAsync(first);
        }

        int taskCount, statusCount, createCount, extendCount;
        using (var afterFirst = CreateContext())
        {
            taskCount = await afterFirst.WorkTasks.CountAsync(t => t.TenantId == DapiTenantId);
            statusCount = await afterFirst.TaskStatuses.CountAsync(s => s.TenantId == DapiTenantId);
            createCount = await afterFirst.TaskCreationRequests.CountAsync(r => r.TenantId == DapiTenantId);
            extendCount = await afterFirst.ObjectiveChangeRequests
                .CountAsync(r => r.TenantId == DapiTenantId && r.RequestType == "extend_allocation");
        }

        using (var second = CreateContext())
        {
            await RunDevSmokeSeederAsync(second);
            await RunDemoSeederAsync(second);
        }

        using var verify = CreateContext();
        (await verify.WorkTasks.CountAsync(t => t.TenantId == DapiTenantId)).Should().Be(taskCount);
        (await verify.TaskStatuses.CountAsync(s => s.TenantId == DapiTenantId)).Should().Be(statusCount);
        (await verify.TaskCreationRequests.CountAsync(r => r.TenantId == DapiTenantId)).Should().Be(createCount);
        (await verify.ObjectiveChangeRequests
            .CountAsync(r => r.TenantId == DapiTenantId && r.RequestType == "extend_allocation"))
            .Should().Be(extendCount);
    }

    private static async Task<List<Guid>> GetLeafObjectiveIdsAsync(ApplicationDbContext db)
    {
        var objectives = await db.Objectives.Where(o => o.TenantId == DapiTenantId).ToListAsync();
        var parentIds = objectives
            .Where(o => o.ParentObjectiveId.HasValue)
            .Select(o => o.ParentObjectiveId!.Value)
            .ToHashSet();
        return objectives.Where(o => !parentIds.Contains(o.Id)).Select(o => o.Id).ToList();
    }

    private sealed class TestClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }
}
