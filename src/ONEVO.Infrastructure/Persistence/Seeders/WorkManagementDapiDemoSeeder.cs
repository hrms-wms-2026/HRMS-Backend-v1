using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Development/Test-only: seeds 22 named demo employees, one "Work Management Team Member" role
/// scoped to exactly the work_management-module permissions, and (via SeedProjectsAndObjectivesAsync
/// in the WorkManagementDapiDemoSeeder.Objectives.cs partial) 5 hand-designed Projects with a
/// 5-layer Objective tree each, all under the existing "dapi" smoke tenant. Must run after
/// DevSmokeTestTenantSeeder (needs the dapi tenant/owner/legal entity/lookups already seeded) and
/// before ProjectsAccessBootstrapSeeder (so its live Roles query - see
/// ProjectsAccessBootstrapSeeder.cs:92-95 - picks up the new Role in the same boot). All inserted
/// rows use deterministic MD5-derived Guids so re-running on every dev boot is a no-op.
/// This does not create schema and must never be treated as production bootstrap.
/// </summary>
public sealed partial class WorkManagementDapiDemoSeeder : IHostedService
{
    private static readonly Guid DapiTenantId = Guid.Parse("6b0874ab-71db-401f-859f-bdd50c1317fb");
    // Kept in sync with DevSmokeTestTenantSeeder.DapiOwnerUserId - see that file's constants block.
    private static readonly Guid DapiOwnerUserId = Guid.Parse("cd49a0c2-e978-4055-b8be-7d46a3727e94");
    private static readonly Guid DapiLegalEntityId = Guid.Parse("57fecfe8-1c1e-4a82-be4b-2c8451436420");

    public static readonly Guid DemoRoleId = DeterministicGuid("dapi-demo:role:team-member");

    private const string DemoUserPassword = "Password123!";
    private const int DemoEmploymentTypeId = 1;
    private const int DemoEmploymentStatusId = 1;
    private const int DemoWorkModeId = 1;

    private readonly IServiceProvider _services;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<WorkManagementDapiDemoSeeder> _logger;

    public WorkManagementDapiDemoSeeder(
        IServiceProvider services,
        IHostEnvironment environment,
        ILogger<WorkManagementDapiDemoSeeder> logger)
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

            tenantContext.SetAdminMode();
            await SeedAsync(db, tenantContext, passwordHasher, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Work Management dapi demo dataset seeded (22 employees, 5 projects).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkManagementDapiDemoSeeder failed. Startup will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static async Task SeedAsync(
        ApplicationDbContext db,
        IWritableTenantContext tenantContext,
        IPasswordHasher passwordHasher,
        CancellationToken ct)
    {
        var dapiTenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == DapiTenantId, ct);
        if (dapiTenant is null)
        {
            // DevSmokeTestTenantSeeder must run first - nothing to attach demo data to yet.
            return;
        }

        tenantContext.SetAdminMode();
        tenantContext.Resolve(new TenantRegistryEntry(
            dapiTenant.Id, dapiTenant.Slug, dapiTenant.Status, PlanCode: null));

        var now = DateTimeOffset.UtcNow;
        var employeeIdByPersonKey = await SeedPersonsAsync(db, passwordHasher, now, ct);
        await SeedRoleAsync(db, now, ct);
        foreach (var person in WorkManagementDapiDemoData.Persons)
        {
            await SeedUserRoleAssignmentAsync(db, person, ct);
        }

        await SeedProjectsAndObjectivesAsync(db, employeeIdByPersonKey, now, ct);
    }

    private static async Task<Dictionary<string, Guid>> SeedPersonsAsync(
        ApplicationDbContext db,
        IPasswordHasher passwordHasher,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var employeeIdByPersonKey = new Dictionary<string, Guid>
        {
            ["dabi"] = await ResolveDapiOwnerEmployeeIdAsync(db, ct)
        };

        foreach (var person in WorkManagementDapiDemoData.Persons)
        {
            var userId = DeterministicGuid($"dapi-demo:user:{person.Key}");

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
            {
                user = new User
                {
                    Id = userId,
                    TenantId = DapiTenantId,
                    Email = person.Email,
                    FirstName = person.FirstName,
                    LastName = person.LastName,
                    PasswordHash = passwordHasher.Hash(DemoUserPassword),
                    IsActive = true,
                    EmailVerified = true,
                    MustChangePassword = false,
                    PasswordSetByAdmin = false,
                    CreatedAt = now,
                    CreatedById = DapiOwnerUserId
                };
                db.Users.Add(user);
            }

            var employeeId = DeterministicGuid($"dapi-demo:employee:{person.Key}");
            var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
            if (employee is null)
            {
                db.Employees.Add(new Employee
                {
                    Id = employeeId,
                    TenantId = DapiTenantId,
                    UserId = userId,
                    EmployeeNumber = person.EmployeeNumber,
                    FirstName = person.FirstName,
                    LastName = person.LastName,
                    Email = person.Email,
                    LegalEntityId = DapiLegalEntityId,
                    EmploymentTypeId = DemoEmploymentTypeId,
                    EmploymentStatusId = DemoEmploymentStatusId,
                    WorkModeId = DemoWorkModeId,
                    HireDate = person.HireDate,
                    CreatedById = DapiOwnerUserId,
                    CreatedAt = now
                });
            }

            employeeIdByPersonKey[person.Key] = employeeId;
        }

        return employeeIdByPersonKey;
    }

    private static async Task<Guid> ResolveDapiOwnerEmployeeIdAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var ownerEmployee = await db.Employees.FirstOrDefaultAsync(e => e.UserId == DapiOwnerUserId, ct);
        if (ownerEmployee is null)
        {
            throw new InvalidOperationException(
                "WorkManagementDapiDemoSeeder requires the dapi tenant owner's Employee row to already " +
                "exist (seeded by DevSmokeTestTenantSeeder) before Work Management demo data can be attached.");
        }
        return ownerEmployee.Id;
    }

    private static async Task SeedRoleAsync(ApplicationDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == DemoRoleId, ct);
        if (role is null)
        {
            role = new Role
            {
                Id = DemoRoleId,
                TenantId = DapiTenantId,
                Name = "Work Management Team Member",
                Description = "Development demo role scoped to Work Management module permissions only.",
                IsSystem = true,
                CreatedAt = now,
                CreatedById = DapiOwnerUserId
            };
            db.Roles.Add(role);
        }

        var workManagementPermissions = await db.Permissions
            .Where(p => p.Module == "work_management")
            .ToListAsync(ct);

        foreach (var permission in workManagementPermissions)
        {
            var alreadyGranted = await db.RolePermissions.AnyAsync(
                rp => rp.TenantId == DapiTenantId && rp.RoleId == DemoRoleId && rp.PermissionId == permission.Id,
                ct);
            if (alreadyGranted)
            {
                continue;
            }

            db.RolePermissions.Add(new RolePermission
            {
                TenantId = DapiTenantId,
                RoleId = DemoRoleId,
                PermissionId = permission.Id
            });
        }
    }

    private static async Task SeedUserRoleAssignmentAsync(
        ApplicationDbContext db,
        DemoPerson person,
        CancellationToken ct)
    {
        var userId = DeterministicGuid($"dapi-demo:user:{person.Key}");
        var exists = await db.UserRoles.AnyAsync(
            ur => ur.TenantId == DapiTenantId && ur.UserId == userId && ur.RoleId == DemoRoleId, ct);
        if (exists)
        {
            return;
        }

        db.UserRoles.Add(new UserRole
        {
            TenantId = DapiTenantId,
            UserId = userId,
            RoleId = DemoRoleId,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedBy = DapiOwnerUserId
        });
    }

    public static Guid DeterministicGuid(string seed)
    {
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return new Guid(hash);
    }
}
