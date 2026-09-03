using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Development/Test-only: lays real org structure (departments, positions, reporting lines,
/// coverage) on top of the "dapi" smoke tenant, which otherwise has an owner and 22 Work
/// Management demo employees with no department/position/role beyond a flat work-management-only
/// role. Also seeds 3 new accounts (General Manager, HR Manager, Operations Executive) under the
/// owner with full profiles, and connects them to the existing "Onexso" project with a task each.
///
/// Must run after DevSmokeTestTenantSeeder (tenant/owner/legal entity) and
/// WorkManagementDapiDemoSeeder (the 22 employees + the 5 projects/objectives/task
/// categories/statuses this seeder attaches new rows to), and before ProjectsAccessBootstrapSeeder
/// (its live Roles query grants "projects:access" to every role that exists in the dapi tenant at
/// that point - see ProjectsAccessBootstrapSeeder.cs - so the new roles seeded here should exist
/// first). All inserted rows use deterministic MD5-derived Guids so re-running on every dev boot is
/// a no-op. This does not create schema and must never be treated as production bootstrap.
/// </summary>
public sealed partial class DapiOrgStructureSeeder : IHostedService
{
    internal static readonly Guid DapiTenantId = Guid.Parse("6b0874ab-71db-401f-859f-bdd50c1317fb");
    // Kept in sync with DevSmokeTestTenantSeeder.DapiOwnerUserId - see that file's constants block.
    internal static readonly Guid DapiOwnerUserId = Guid.Parse("cd49a0c2-e978-4055-b8be-7d46a3727e94");
    internal static readonly Guid DapiLegalEntityId = Guid.Parse("57fecfe8-1c1e-4a82-be4b-2c8451436420");

    internal const string NewHirePassword = "Password123!";
    internal const int DefaultEmploymentTypeId = 1;
    internal const int DefaultEmploymentStatusId = 1;
    internal const int DefaultWorkModeId = 1;

    private readonly IServiceProvider _services;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DapiOrgStructureSeeder> _logger;

    public DapiOrgStructureSeeder(
        IServiceProvider services,
        IHostEnvironment environment,
        ILogger<DapiOrgStructureSeeder> logger)
    {
        _services = services;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment() && !_environment.IsEnvironment("Test"))
        {
            return;
        }

        try
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var closures = scope.ServiceProvider.GetRequiredService<IEmployeeHierarchyClosureRepository>();

            tenantContext.SetAdminMode();
            await SeedAsync(db, tenantContext, passwordHasher, closures, cancellationToken);
            _logger.LogInformation(
                "Dapi org structure seeded (8 departments, 16 positions, 4 roles, 3 new accounts).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DapiOrgStructureSeeder failed. Startup will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static async Task SeedAsync(
        ApplicationDbContext db,
        IWritableTenantContext tenantContext,
        IPasswordHasher passwordHasher,
        IEmployeeHierarchyClosureRepository closures,
        CancellationToken ct)
    {
        var dapiTenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == DapiTenantId, ct);
        if (dapiTenant is null)
        {
            // DevSmokeTestTenantSeeder must run first - nothing to attach org structure to yet.
            return;
        }

        tenantContext.SetAdminMode();
        tenantContext.Resolve(new TenantRegistryEntry(
            dapiTenant.Id, dapiTenant.Slug, dapiTenant.Status, PlanCode: null));

        var now = DateTimeOffset.UtcNow;

        var departmentIdByCode = await SeedDepartmentsAsync(db, now, ct);
        var positionIdByCode = await SeedPositionsAsync(db, departmentIdByCode, now, ct);
        var roleIdByName = await SeedRolesAsync(db, now, ct);

        await SeedNewAccountsAsync(
            db, passwordHasher, departmentIdByCode, positionIdByCode, roleIdByName, now, ct);
        await BackfillOwnerAsync(db, departmentIdByCode, positionIdByCode, now, ct);
        await RestructureExistingEmployeesAsync(
            db, departmentIdByCode, positionIdByCode, roleIdByName, now, ct);

        // Flush before rebuilding the closure: RebuildAsync reads Positions/PositionAssignments
        // with AsNoTracking, which hits the database directly and would miss anything still only
        // in the change tracker.
        await db.SaveChangesAsync(ct);
        await closures.RebuildAsync(DapiTenantId, ct);

        await ConnectNewAccountsToProjectAsync(db, now, ct);
        await db.SaveChangesAsync(ct);
    }

    internal static Guid DeterministicGuid(string seed) =>
        WorkManagementDapiDemoSeeder.DeterministicGuid(seed);
}
