using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Task 06 Part B: a UAT-sized data set on the Dapi demo tenant, for the endpoint timing baseline. Off unless
/// <c>Seeding:PerformanceBaseline:Enabled</c> is true, and only in Development. Use it on a throwaway database:
/// it adds about 475 employees (to 500 in all), 45 projects (to 50), tasks up to 20,000 and a year of attendance.
///
/// The people sit under the six existing team leads: 4 sub-team leads per team, each with a pool of engineers, so the
/// owner's reporting line is five levels deep. Every row has a deterministic id and the seeder only fills what is
/// missing, so a re-run is quick. <see cref="DapiHistorySeeder"/> leaves these people out (their employee number
/// starts with <see cref="EmployeeNumberPrefix"/>); it would otherwise write months of monitoring rows for each.
/// </summary>
public sealed partial class DapiPerformanceBaselineSeeder : IHostedService
{
    public const string ConfigKey = "Seeding:PerformanceBaseline:Enabled";
    internal const string EmployeeNumberPrefix = "PERF-";
    internal const string EmailDomain = "perf.dapi.test";
    internal const int TargetEmployees = 500;
    internal const int TargetProjects = 50;
    internal const int TargetTasks = 20_000;
    internal const int AttendanceDays = 365;

    private const int LeadsPerTeam = 4;
    private const int PoolOccupancy = 25;
    private const int SaveBatch = 2_000;

    private static readonly Guid DapiTenantId = DapiOrgStructureSeeder.DapiTenantId;
    private static readonly Guid DapiOwnerUserId = DapiOrgStructureSeeder.DapiOwnerUserId;
    private static readonly Guid DapiLegalEntityId = DapiOrgStructureSeeder.DapiLegalEntityId;

    /// <summary>The six existing team-lead positions and their departments (DapiOrgStructureData.Teams).</summary>
    private static readonly (string Department, string LeadPosition)[] Teams =
    [
        ("EPOS", "EPLD"), ("EVTIX", "EVLD"), ("ONEXSO", "ONLD"), ("WCRAFT", "WCLD"), ("HWINT", "HILD"), ("MKT", "MKLD")
    ];

    private static readonly string[] FirstNames =
    [
        "Arun", "Bavani", "Chandran", "Dilani", "Eesan", "Fathima", "Gowri", "Haran", "Indu", "Janani",
        "Kavin", "Lakshi", "Mohan", "Nila", "Oviya", "Pirathap", "Ramya", "Sajeev", "Thanu", "Uma",
        "Vithu", "Yalini", "Aathi", "Kumaran", "Selvi"
    ];

    private static readonly string[] LastNames =
    [
        "Arulanantham", "Balasingam", "Chelliah", "Dharmalingam", "Ganeshan", "Jeyakumar", "Kanagaratnam",
        "Loganathan", "Mahendran", "Nadarajah", "Paramsothy", "Ratnam", "Sivakumar", "Thevarajah", "Vigneswaran"
    ];

    private readonly IServiceProvider _services;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DapiPerformanceBaselineSeeder> _logger;

    public DapiPerformanceBaselineSeeder(
        IServiceProvider services,
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger<DapiPerformanceBaselineSeeder> logger)
    {
        _services = services;
        _environment = environment;
        _configuration = configuration;
        _logger = logger;
    }

    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() && configuration.GetValue<bool>(ConfigKey);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled(_environment, _configuration))
            return;

        try
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var closures = scope.ServiceProvider.GetRequiredService<IEmployeeHierarchyClosureRepository>();

            var started = DateTimeOffset.UtcNow;
            var summary = await SeedAsync(db, tenantContext, passwordHasher, closures, DateTimeOffset.UtcNow, cancellationToken);
            _logger.LogInformation(
                "Performance baseline data seeded in {Seconds:0}s: {Summary}.",
                (DateTimeOffset.UtcNow - started).TotalSeconds, summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DapiPerformanceBaselineSeeder failed. Startup will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static async Task<string> SeedAsync(
        ApplicationDbContext db,
        IWritableTenantContext tenantContext,
        IPasswordHasher passwordHasher,
        IEmployeeHierarchyClosureRepository closures,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == DapiTenantId, ct);
        if (tenant is null)
            return "no Dapi tenant";

        tenantContext.SetAdminMode();
        tenantContext.Resolve(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null));

        var tracking = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            var people = await SeedPeopleAsync(db, passwordHasher, closures, now, ct);
            var work = await SeedWorkAsync(db, people, now, ct);
            var attendance = await SeedAttendanceAsync(db, now, ct);
            return $"{people.Added} employees, {work}, {attendance} attendance days";
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = tracking;
        }
    }

    /// <summary>One seeded person: their employee and user ids and whether they lead a sub-team.</summary>
    internal sealed record SeededPerson(Guid EmployeeId, Guid UserId, string Team, int LeadIndex, bool IsLead);

    internal sealed record SeededPeople(IReadOnlyList<SeededPerson> All, int Added);

    private static async Task<SeededPeople> SeedPeopleAsync(
        ApplicationDbContext db,
        IPasswordHasher passwordHasher,
        IEmployeeHierarchyClosureRepository closures,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var baseCount = await db.Employees.CountAsync(
            e => e.TenantId == DapiTenantId && !e.EmployeeNumber.StartsWith(EmployeeNumberPrefix), ct);
        var toAdd = Math.Max(0, TargetEmployees - baseCount);
        var leadCount = Teams.Length * LeadsPerTeam;
        var engineerCount = Math.Max(0, toAdd - leadCount);

        var departmentIds = await db.Departments
            .Where(d => d.TenantId == DapiTenantId && d.LegalEntityId == DapiLegalEntityId)
            .ToDictionaryAsync(d => d.Code!, d => d.Id, ct);
        var roleEmployee = DapiOrgStructureSeeder.DeterministicGuid($"dapi-org:role:{DapiOrgStructureData.RoleEmployee}");
        var roleManager = DapiOrgStructureSeeder.DeterministicGuid($"dapi-org:role:{DapiOrgStructureData.RoleManager}");
        var workModeId = await db.TimeAttendanceWorkModes
            .Where(w => w.TenantId == DapiTenantId && w.LegalEntityId == DapiLegalEntityId
                && w.Name == DapiOrgStructureSeeder.DefaultWorkModeName && w.IsActive)
            .Select(w => (Guid?)w.Id)
            .FirstOrDefaultAsync(ct);

        var existingEmployees = (await db.Employees
                .Where(e => e.TenantId == DapiTenantId && e.EmployeeNumber.StartsWith(EmployeeNumberPrefix))
                .Select(e => e.Id)
                .ToListAsync(ct))
            .ToHashSet();
        var positionIds = Enumerable.Range(0, Teams.Length * LeadsPerTeam)
            .SelectMany(slot => new[] { Id($"position:{LeadCode(slot)}"), Id($"position:{PoolCode(slot)}") })
            .ToList();
        var existingPositions = (await db.Positions
                .Where(p => p.TenantId == DapiTenantId && positionIds.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync(ct))
            .ToHashSet();

        // One hash for every seeded account: hashing 475 passwords one by one takes about a minute.
        string? passwordHash = null;
        var people = new List<SeededPerson>();
        var added = 0;
        var hireDate = DateOnly.FromDateTime(now.UtcDateTime.Date).AddDays(-AttendanceDays - 30);

        for (var teamIndex = 0; teamIndex < Teams.Length; teamIndex++)
        {
            var (department, leadPosition) = Teams[teamIndex];
            var departmentId = departmentIds[department];
            var teamLeadPositionId = DapiOrgStructureSeeder.DeterministicGuid($"dapi-org:position:{leadPosition}");

            for (var leadIndex = 0; leadIndex < LeadsPerTeam; leadIndex++)
            {
                var slot = (teamIndex * LeadsPerTeam) + leadIndex;
                var leadPositionId = AddPosition(db, existingPositions, LeadCode(slot),
                    $"{department} Squad Lead {leadIndex + 1}", departmentId, teamLeadPositionId, 1, now);
                var poolPositionId = AddPosition(db, existingPositions, PoolCode(slot),
                    $"{department} Squad Engineer {leadIndex + 1}", departmentId, leadPositionId, PoolOccupancy, now);

                if (slot < toAdd)
                {
                    var lead = Person(slot, department, leadIndex, isLead: true);
                    people.Add(lead);
                    if (AddPerson(db, existingEmployees, lead, slot, departmentId, leadPositionId, workModeId,
                            [roleEmployee, roleManager], hireDate, ref passwordHash, passwordHasher, now))
                        added++;
                }

                // Engineers are spread evenly over the 24 pools.
                for (var n = slot; n < engineerCount; n += leadCount)
                {
                    var index = leadCount + n;
                    var engineer = Person(index, department, leadIndex, isLead: false);
                    people.Add(engineer);
                    if (AddPerson(db, existingEmployees, engineer, index, departmentId, poolPositionId, workModeId,
                            [roleEmployee], hireDate, ref passwordHash, passwordHasher, now))
                        added++;
                }
            }
        }

        if (added > 0 || db.ChangeTracker.HasChanges())
        {
            db.ChangeTracker.DetectChanges();
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            await closures.RebuildAsync(DapiTenantId, ct);
        }

        return new SeededPeople(people, added);
    }

    // Position codes are at most 5 characters.
    private static string LeadCode(int slot) => $"PL{slot:00}";

    private static string PoolCode(int slot) => $"PE{slot:00}";

    private static SeededPerson Person(int index, string team, int leadIndex, bool isLead) => new(
        Id($"employee:{index}"), Id($"user:{index}"), team, leadIndex, isLead);

    private static Guid AddPosition(
        ApplicationDbContext db, HashSet<Guid> existing, string code, string name, Guid departmentId,
        Guid reportsToPositionId, int occupancy, DateTimeOffset now)
    {
        var id = Id($"position:{code}");
        if (!existing.Add(id))
            return id;

        db.Positions.Add(new Position
        {
            Id = id,
            TenantId = DapiTenantId,
            LegalEntityId = DapiLegalEntityId,
            DepartmentId = departmentId,
            Code = code,
            Name = name,
            PositionType = occupancy == 1 ? Position.TypeUnique : Position.TypePooled,
            MaxOccupancy = occupancy,
            ReportsToPositionId = reportsToPositionId,
            IsActive = true,
            CreatedAt = now,
            CreatedById = DapiOwnerUserId
        });
        // The same side rows CreatePositionCommandHandler writes: reporting history and the locked coverage record.
        db.PositionReportingHistories.Add(new PositionReportingHistory
        {
            Id = Id($"reporting-history:{code}"),
            TenantId = DapiTenantId,
            PositionId = id,
            ReportsToPositionId = reportsToPositionId,
            EffectiveFrom = DateOnly.FromDateTime(now.UtcDateTime.Date),
            CreatedAt = now,
            CreatedByUserId = DapiOwnerUserId
        });
        db.ManagementCoverageRecords.Add(new ManagementCoverageRecord
        {
            Id = Id($"coverage:{code}"),
            TenantId = DapiTenantId,
            LegalEntityId = DapiLegalEntityId,
            OwnerPositionId = reportsToPositionId,
            CoveredTargetType = ManagementCoverageRecord.TargetPosition,
            CoveredPositionId = id,
            OwnerOrder = 1,
            Source = ManagementCoverageRecord.SourceReportingStructure,
            IsLocked = true,
            Status = ManagementCoverageRecord.StatusActive,
            CreatedAt = now
        });
        return id;
    }

    private static bool AddPerson(
        ApplicationDbContext db, HashSet<Guid> existing, SeededPerson person, int index, Guid departmentId,
        Guid positionId, Guid? workModeId, Guid[] roleIds, DateOnly hireDate, ref string? passwordHash,
        IPasswordHasher passwordHasher, DateTimeOffset now)
    {
        if (!existing.Add(person.EmployeeId))
            return false;

        passwordHash ??= passwordHasher.Hash(DapiOrgStructureSeeder.NewHirePassword);
        var first = FirstNames[index % FirstNames.Length];
        var last = LastNames[(index / FirstNames.Length) % LastNames.Length];
        var email = $"perf{index + 1:000}@{EmailDomain}";

        db.Users.Add(new User
        {
            Id = person.UserId,
            TenantId = DapiTenantId,
            Email = email,
            FirstName = first,
            LastName = last,
            PasswordHash = passwordHash,
            IsActive = true,
            EmailVerified = true,
            MustChangePassword = false,
            PasswordSetByAdmin = false,
            CreatedAt = now,
            CreatedById = DapiOwnerUserId
        });
        db.Employees.Add(new Employee
        {
            Id = person.EmployeeId,
            TenantId = DapiTenantId,
            UserId = person.UserId,
            EmployeeNumber = $"{EmployeeNumberPrefix}{index + 1:000}",
            FirstName = first,
            LastName = last,
            Email = email,
            LegalEntityId = DapiLegalEntityId,
            DepartmentId = departmentId,
            EmploymentTypeId = DapiOrgStructureSeeder.DefaultEmploymentTypeId,
            EmploymentStatusId = DapiOrgStructureSeeder.DefaultEmploymentStatusId,
            WorkModeId = workModeId,
            HireDate = hireDate.AddDays(index % 60),
            CreatedById = DapiOwnerUserId,
            CreatedAt = now
        });
        db.PositionAssignments.Add(new PositionAssignment
        {
            Id = Id($"assignment:{index}"),
            TenantId = DapiTenantId,
            EmployeeId = person.EmployeeId,
            PositionId = positionId,
            AssignmentKind = PositionAssignmentKind.PrimaryEmployment,
            EffectiveFrom = hireDate,
            AssignmentStatus = PositionAssignmentStatus.Active,
            CreatedAt = now,
            CreatedById = DapiOwnerUserId
        });
        foreach (var roleId in roleIds)
        {
            db.UserRoles.Add(new UserRole
            {
                TenantId = DapiTenantId,
                UserId = person.UserId,
                RoleId = roleId,
                AssignedAt = now,
                AssignedBy = DapiOwnerUserId
            });
        }

        return true;
    }

    private static async Task SaveBatchAsync(ApplicationDbContext db, CancellationToken ct)
    {
        db.ChangeTracker.DetectChanges();
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private static Guid Id(string key) => WorkManagementDapiDemoSeeder.DeterministicGuid($"perf-baseline:{key}");

    /// <summary>A stable pseudo-random pick in 0..(max-1), so the data set is the same on every machine.</summary>
    private static int Pick(int seed, int max)
    {
        unchecked
        {
            var x = (uint)seed;
            x = ((x >> 16) ^ x) * 0x45d9f3bu;
            x = ((x >> 16) ^ x) * 0x45d9f3bu;
            x = (x >> 16) ^ x;
            return (int)(x % (uint)max);
        }
    }
}
