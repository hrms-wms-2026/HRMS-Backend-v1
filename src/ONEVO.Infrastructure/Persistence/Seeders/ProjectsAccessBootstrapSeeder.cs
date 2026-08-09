using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Development-only, targeted permission grant for a single named test account. Deliberately
/// bypasses the module-driven entitlement system (PermissionSeeder's per-permission module tags,
/// DefaultRoleSeeder/ModuleEntitlementService's active_modules matching) rather than changing it -
/// active_modules and the shared permission catalog stay exactly as-is, per 2026-08-09 direction.
/// Grants projects:access directly to whatever role(s) this one user is already assigned, so no
/// other tenant or user is affected. No-op if the user doesn't exist yet (e.g. before they sign
/// up) - safe to leave running; a later signup under this email picks up the grant on the next
/// backend restart, since role_permissions rows are seeded once, not recomputed live.
/// </summary>
public sealed class ProjectsAccessBootstrapSeeder : IHostedService
{
    private const string TargetEmail = "dapiyshanth1908@gmail.com";
    private const string TargetPermissionCode = "projects:access";

    private readonly IServiceProvider _services;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ProjectsAccessBootstrapSeeder> _logger;

    public ProjectsAccessBootstrapSeeder(
        IServiceProvider services,
        IHostEnvironment environment,
        ILogger<ProjectsAccessBootstrapSeeder> logger)
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

            tenantContext.SetAdminMode();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == TargetEmail, cancellationToken);
            if (user is null)
            {
                _logger.LogInformation(
                    "ProjectsAccessBootstrapSeeder: no user found for {Email} yet - nothing to grant.",
                    TargetEmail);
                return;
            }

            var permission = await db.Permissions
                .FirstOrDefaultAsync(p => p.Code == TargetPermissionCode, cancellationToken);
            if (permission is null)
            {
                _logger.LogWarning(
                    "ProjectsAccessBootstrapSeeder: permission code {Code} not found - PermissionSeeder must run before this seeder.",
                    TargetPermissionCode);
                return;
            }

            var roleIds = await db.UserRoles
                .Where(ur => ur.TenantId == user.TenantId && ur.UserId == user.Id)
                .Select(ur => ur.RoleId)
                .ToListAsync(cancellationToken);

            if (roleIds.Count == 0)
            {
                _logger.LogWarning(
                    "ProjectsAccessBootstrapSeeder: {Email} has no assigned roles yet - nothing to grant.",
                    TargetEmail);
                return;
            }

            var grantedCount = 0;
            foreach (var roleId in roleIds)
            {
                var alreadyGranted = await db.RolePermissions.AnyAsync(
                    rp => rp.TenantId == user.TenantId && rp.RoleId == roleId && rp.PermissionId == permission.Id,
                    cancellationToken);
                if (alreadyGranted)
                {
                    continue;
                }

                db.RolePermissions.Add(new RolePermission
                {
                    TenantId = user.TenantId,
                    RoleId = roleId,
                    PermissionId = permission.Id
                });
                grantedCount++;
            }

            if (grantedCount > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation(
                "ProjectsAccessBootstrapSeeder: {Code} granted to {GrantedCount} of {RoleCount} role(s) for {Email} ({AlreadyHadIt} already had it).",
                TargetPermissionCode, grantedCount, roleIds.Count, TargetEmail, roleIds.Count - grantedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProjectsAccessBootstrapSeeder failed. Startup will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
