using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.Helpers;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Development-only, tenant-scoped Work Management unlock for the "dapi" tenant (identified via
/// dapiyshanth1908@gmail.com, since a tenant slug isn't known until the tenant/user exist).
///
/// Two things are required together — a role_permissions row alone is NOT enough, confirmed by
/// reading PermissionResolver.ResolveAsync: every role-granted permission is re-filtered by
/// `activeModules.Contains(row.Module)` on every single request (Step 2), not just at seed time.
/// projects:access's Module column is "work_management" (PermissionSeeder's existing tag, left
/// untouched per 2026-08-09 direction — the shared catalog is not touched here), which never
/// appears in any tenant's active_modules (subscription SelectedModulesJson uses canonical Phase 1
/// keys like "projects"), so a role_permissions row for it is silently filtered out of every
/// /auth/me response regardless of whether the row exists. So:
///
/// 1. Add "work_management" to ONLY this one tenant's active subscription's SelectedModulesJson -
///    other tenants' subscriptions are untouched, so no other tenant's users are affected.
/// 2. Grant projects:access to every Role in that tenant (not just the one user's role), per
///    2026-08-09 direction to cover "the dapi tenant's roles", not just one account.
///
/// No-op if the user doesn't exist yet; safe to leave running - a later signup under this email
/// picks up the grant on the next backend restart, since none of this is recomputed live.
/// </summary>
public sealed class ProjectsAccessBootstrapSeeder : IHostedService
{
    private const string TargetEmail = "dapiyshanth1908@gmail.com";
    private const string TargetPermissionCode = "projects:access";
    private const string TargetModuleKey = "work_management";

    private static readonly HashSet<string> ActiveSubscriptionStatuses =
        new(SubscriptionStatusRules.ActiveStatuses, StringComparer.OrdinalIgnoreCase);

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

            var moduleUnlocked = await EnsureTenantModuleUnlockedAsync(db, user.TenantId, cancellationToken);

            var roleIds = await db.Roles
                .Where(r => r.TenantId == user.TenantId)
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);

            if (roleIds.Count == 0)
            {
                _logger.LogWarning(
                    "ProjectsAccessBootstrapSeeder: tenant {TenantId} has no roles yet - nothing to grant.",
                    user.TenantId);
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

            if (grantedCount > 0 || moduleUnlocked)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation(
                "ProjectsAccessBootstrapSeeder: tenant {TenantId} - module unlock {ModuleUnlocked}, {Code} granted to {GrantedCount} of {RoleCount} role(s) ({AlreadyHadIt} already had it).",
                user.TenantId, moduleUnlocked ? "applied" : "already present", TargetPermissionCode,
                grantedCount, roleIds.Count, roleIds.Count - grantedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProjectsAccessBootstrapSeeder failed. Startup will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <returns>true if SelectedModulesJson was changed (module was missing and got added).</returns>
    private static async Task<bool> EnsureTenantModuleUnlockedAsync(
        ApplicationDbContext db,
        Guid tenantId,
        CancellationToken ct)
    {
        var subscription = await db.TenantSubscriptions
            .Where(s => s.TenantId == tenantId && ActiveSubscriptionStatuses.Contains(s.Status))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (subscription is null)
        {
            return false;
        }

        List<string> modules;
        try
        {
            modules = JsonSerializer.Deserialize<List<string>>(subscription.SelectedModulesJson) ?? [];
        }
        catch (JsonException)
        {
            modules = [];
        }

        if (modules.Contains(TargetModuleKey, StringComparer.Ordinal))
        {
            return false;
        }

        modules.Add(TargetModuleKey);
        subscription.SelectedModulesJson = JsonSerializer.Serialize(modules);
        subscription.UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }
}
