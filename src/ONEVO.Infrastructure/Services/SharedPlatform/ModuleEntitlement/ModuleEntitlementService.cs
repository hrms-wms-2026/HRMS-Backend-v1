using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.SharedPlatform;

public sealed class ModuleEntitlementService : IModuleEntitlementService
{
    private static readonly HashSet<string> ActiveSubscriptionStatuses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "active",
            "trialing",
            "maintenance_included",
            "subscription_included"
        };

    private readonly ApplicationDbContext _db;

    public ModuleEntitlementService(ApplicationDbContext db) => _db = db;

    public async Task<bool> IsModuleEnabledAsync(
        Guid tenantId,
        string moduleKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(moduleKey))
            return false;

        var activeModuleKeys = await GetActiveModuleKeysForTenantAsync(tenantId, ct);
        return activeModuleKeys.Contains(moduleKey.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<Permission>> GetEntitledPermissionsAsync(
        IReadOnlyList<string> moduleKeys,
        CancellationToken ct = default)
    {
        return await _db.Permissions
            .AsNoTracking()
            .Where(p => moduleKeys.Contains(p.Module))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetActiveModuleKeysForTenantAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        var subscription = await _db.TenantSubscriptions
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && ActiveSubscriptionStatuses.Contains(s.Status))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (subscription is null)
            return [];

        try
        {
            var modules = JsonSerializer.Deserialize<List<string>>(subscription.SelectedModulesJson);
            if (modules is null || modules.Count == 0)
                return [];

            return modules
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<Permission>> GetAssignablePermissionsForTenantAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        var moduleKeys = await GetActiveModuleKeysForTenantAsync(tenantId, ct);
        if (moduleKeys.Count == 0)
            return [];

        var keys = moduleKeys.ToList();

        // EF cannot translate ModuleAutoGrants.Contains; materialize the codes so the
        // filter becomes a SQL NOT IN.
        var autoGrantCodes = ModuleAutoGrants.ByModule.Values
            .SelectMany(v => v)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return await _db.Permissions
            .AsNoTracking()
            .Where(p =>
                keys.Contains(p.Module)
                && p.Code != "*"
                && !autoGrantCodes.Contains(p.Code))
            .OrderBy(p => p.Module)
            .ThenBy(p => p.Code)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Permission>> GetAssignablePermissionsForTenantAsync(
        Guid tenantId,
        IReadOnlyList<Guid> permissionIds,
        CancellationToken ct = default)
    {
        if (permissionIds.Count == 0)
            return [];

        var assignable = await GetAssignablePermissionsForTenantAsync(tenantId, ct);
        var idSet = permissionIds.ToHashSet();

        return assignable
            .Where(p => idSet.Contains(p.Id))
            .OrderBy(p => p.Module)
            .ThenBy(p => p.Code)
            .ToList();
    }
}
