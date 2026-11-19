using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.Lifecycle;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.Persistence.SqlServer;
using ONEVO.Infrastructure.Services.DevPlatform.Tenancy;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Brings the permissions table in line with the catalogue and PermissionLifecycle, without losing
/// grants: a rename updates the row in place (grants and overrides point at the row id, so they
/// follow it and the delete cascade never fires), a new code with a default grant is backfilled once
/// to matching roles of existing tenants, and a retirement refuses to run while the code is still
/// granted unless the declaration says DropGrants.
///
/// The caller must put the tenant context in admin mode first: role_permissions and
/// user_permission_overrides are under RLS, which applies to every login on SQL Server, so in any
/// other mode the grant queries see nothing and the run would silently move nothing.
/// </summary>
public sealed class PermissionCatalogSynchronizer
{
    public const string AppLockResource = "permission-catalog-sync";
    private const int AppLockTimeoutMilliseconds = 20_000;

    private readonly ApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger _logger;

    public PermissionCatalogSynchronizer(ApplicationDbContext db, IDateTimeProvider clock, ILogger logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task<PermissionSyncReport> SyncAsync(
        IReadOnlyList<PermissionDefinition> catalogue,
        PermissionLifecycleRules rules,
        CancellationToken ct = default)
    {
        var errors = PermissionLifecycleValidator.Validate(catalogue, rules);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Permission lifecycle declarations are invalid: " + string.Join(" ", errors));
        }

        if (!_db.Database.IsRelational())
            return await RunAsync(catalogue, rules, ct);

        // One transaction: a refused retirement rolls back every earlier step. The app lock makes two
        // instances starting together run one after the other instead of racing on the same rows.
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            if (_db.Database.IsSqlServer()
                && !await SqlAppLock.TryAcquireAsync(_db, AppLockResource, AppLockTimeoutMilliseconds, ct))
            {
                _logger.LogWarning(
                    "Permission catalogue sync skipped: another instance held {Lock} for {Timeout} ms.",
                    AppLockResource, AppLockTimeoutMilliseconds);
                return new PermissionSyncReport { Skipped = true };
            }

            var report = await RunAsync(catalogue, rules, ct);
            await transaction.CommitAsync(ct);
            return report;
        });
    }

    private async Task<PermissionSyncReport> RunAsync(
        IReadOnlyList<PermissionDefinition> catalogue, PermissionLifecycleRules rules, CancellationToken ct)
    {
        var run = new Run(_clock.UtcNow)
        {
            // The one read past the retired-code filter: Permission is a global table (no tenant_id),
            // so this bypasses only the soft-retire filter, never tenant scoping.
            ByCode = (await _db.Permissions.IgnoreQueryFilters().ToListAsync(ct))
                .ToDictionary(p => p.Code, StringComparer.Ordinal),
            Ledger = (await _db.PermissionChanges.Select(c => c.ChangeKey).ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal),
        };

        await ApplyRenamesAsync(rules, run, ct);
        ApplyCatalogue(catalogue, run);
        await _db.SaveChangesAsync(ct);

        await ApplyDefaultGrantsAsync(rules, run, ct);
        await ApplyRetirementsAsync(rules, run, ct);
        await _db.SaveChangesAsync(ct);

        var report = run.Report;
        if (report.HasChanges)
        {
            _logger.LogInformation(
                "Permission catalogue synced: {Added} added, {Updated} updated, {Restored} restored, {Renamed} renamed, " +
                "{DefaultGrants} default grants added, {SoftRetired} soft-retired, {Removed} removed.",
                report.Added, report.Updated, report.Restored, report.Renamed,
                report.DefaultGrantsAdded, report.SoftRetired, report.Removed);
        }
        else
        {
            _logger.LogInformation("Permission catalogue already in sync - nothing to change.");
        }

        return report;
    }

    private async Task ApplyRenamesAsync(PermissionLifecycleRules rules, Run run, CancellationToken ct)
    {
        var map = rules.Renames.ToDictionary(r => r.From, r => r.To, StringComparer.Ordinal);
        foreach (var rename in rules.Renames)
        {
            if (!run.ByCode.TryGetValue(rename.From, out var source))
                continue;

            var to = PermissionLifecycleValidator.ResolveRename(rename.From, map, out _);
            var holders = await CountHoldersAsync(source.Id, ct);
            string mode;
            if (run.ByCode.TryGetValue(to, out var target))
            {
                // The new code was already seeded (an earlier build added it before the rename was
                // declared): move the grants onto it, then drop the old row, which by then has none.
                await MergeGrantsAsync(source, target, ct);
                _db.Permissions.Remove(source);
                mode = "merged";
            }
            else
            {
                source.Code = to;
                run.ByCode[to] = source;
                mode = "in-place";
            }

            run.ByCode.Remove(rename.From);
            await RenameInRoleTemplatesAsync(rename.From, to, ct);
            run.Report.Renamed++;

            _logger.LogInformation(
                "Renamed permission {From} -> {To} ({Mode}); kept {RoleGrants} role grants and {Overrides} user overrides " +
                "in {Tenants} tenants.",
                rename.From, to, mode, holders.RoleGrants, holders.Overrides, holders.Tenants.Count);
            Record(run, $"rename:{rename.From}->{to}", "rename", to, rename.From,
                new { mode, rename.Reason, rename.Date, holders.RoleGrants, holders.Overrides, tenants = holders.Tenants });
        }
    }

    private async Task MergeGrantsAsync(Permission source, Permission target, CancellationToken ct)
    {
        var targetRoles = (await _db.RolePermissions
                .Where(rp => rp.PermissionId == target.Id)
                .Select(rp => rp.RoleId)
                .ToListAsync(ct))
            .ToHashSet();
        foreach (var grant in await _db.RolePermissions.Where(rp => rp.PermissionId == source.Id).ToListAsync(ct))
        {
            if (targetRoles.Add(grant.RoleId))
            {
                _db.RolePermissions.Add(new RolePermission
                {
                    TenantId = grant.TenantId, RoleId = grant.RoleId, PermissionId = target.Id,
                });
            }

            _db.RolePermissions.Remove(grant);
        }

        // An override already on the new code is the newer decision about it, so it wins.
        var targetUsers = (await _db.UserPermissionOverrides
                .Where(o => o.PermissionId == target.Id)
                .Select(o => new { o.TenantId, o.UserId })
                .ToListAsync(ct))
            .Select(o => (o.TenantId, o.UserId))
            .ToHashSet();
        foreach (var userOverride in await _db.UserPermissionOverrides.Where(o => o.PermissionId == source.Id).ToListAsync(ct))
        {
            if (targetUsers.Add((userOverride.TenantId, userOverride.UserId)))
                userOverride.PermissionId = target.Id;
            else
                _db.UserPermissionOverrides.Remove(userOverride);
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task RenameInRoleTemplatesAsync(string from, string to, CancellationToken ct)
    {
        foreach (var template in await _db.RoleTemplates.ToListAsync(ct))
        {
            var codes = JsonSerializer.Deserialize<List<string>>(template.PermissionCodesJson) ?? [];
            if (!codes.Contains(from, StringComparer.Ordinal))
                continue;

            var renamed = codes.Select(c => c == from ? to : c).Distinct(StringComparer.Ordinal).ToList();
            template.PermissionCodesJson = JsonSerializer.Serialize(renamed);
            template.UpdatedAt = _clock.UtcNow;
        }
    }

    private void ApplyCatalogue(IReadOnlyList<PermissionDefinition> catalogue, Run run)
    {
        foreach (var definition in catalogue)
        {
            if (run.ByCode.TryGetValue(definition.Code, out var row))
            {
                if (row.RetiredAt is not null)
                {
                    // Back in the catalogue (a retirement was reverted): its grants were kept, so they
                    // work again as soon as the filter stops hiding the row.
                    row.RetiredAt = null;
                    run.Report.Restored++;
                }

                if (row.Module != definition.Module || row.Description != definition.Description)
                {
                    row.Module = definition.Module;
                    row.Description = definition.Description;
                    run.Report.Updated++;
                }

                continue;
            }

            row = new Permission
            {
                Id = Guid.NewGuid(),
                Code = definition.Code,
                Description = definition.Description,
                Module = definition.Module,
            };
            _db.Permissions.Add(row);
            run.ByCode[definition.Code] = row;
            run.Report.Added++;
        }
    }

    private async Task ApplyDefaultGrantsAsync(PermissionLifecycleRules rules, Run run, CancellationToken ct)
    {
        foreach (var grant in rules.DefaultGrants)
        {
            var key = $"default-grant:{grant.Code}";
            if (run.Ledger.Contains(key) || !run.ByCode.TryGetValue(grant.Code, out var permission))
                continue;

            var roles = new Dictionary<Guid, Guid>(); // role id -> tenant id
            foreach (var target in grant.Targets)
            {
                foreach (var (roleId, tenantId) in await FindTargetRolesAsync(target, run, ct))
                    roles.TryAdd(roleId, tenantId);
            }

            var holding = (await _db.RolePermissions
                    .Where(rp => rp.PermissionId == permission.Id)
                    .Select(rp => rp.RoleId)
                    .ToListAsync(ct))
                .ToHashSet();
            var added = roles.Where(r => !holding.Contains(r.Key)).ToList();
            foreach (var (roleId, tenantId) in added)
            {
                _db.RolePermissions.Add(new RolePermission
                {
                    TenantId = tenantId, RoleId = roleId, PermissionId = permission.Id,
                });
            }

            var byTenant = added
                .GroupBy(r => r.Value)
                .ToDictionary(g => g.Key.ToString(), g => g.Select(r => r.Key).OrderBy(id => id).ToList());
            foreach (var (tenantId, roleIds) in byTenant)
            {
                _logger.LogInformation(
                    "Default grant {Code}: added to {Count} roles in tenant {TenantId}.",
                    grant.Code, roleIds.Count, tenantId);
            }

            run.Report.DefaultGrantsAdded += added.Count;
            Record(run, key, "default-grant", grant.Code, null, new
            {
                targets = grant.Targets.Select(t => t.ToString()).ToList(),
                grant.Reason,
                grant.Date,
                added = added.Count,
                tenants = byTenant,
            });
        }
    }

    private async Task<IReadOnlyList<(Guid RoleId, Guid TenantId)>> FindTargetRolesAsync(
        DefaultGrantTarget target, Run run, CancellationToken ct)
    {
        switch (target.Kind)
        {
            case DefaultGrantTargetKind.OwnerRole:
                return (await _db.Roles
                        .Where(r => r.IsSystem && r.Name == DefaultRoleSeeder.OwnerRoleName)
                        .Select(r => new { r.Id, r.TenantId })
                        .ToListAsync(ct))
                    .Select(r => (r.Id, r.TenantId)).ToList();
            case DefaultGrantTargetKind.RoleTemplate:
                return (await _db.Roles
                        .Where(r => r.SourceTemplateId == target.TemplateId)
                        .Select(r => new { r.Id, r.TenantId })
                        .ToListAsync(ct))
                    .Select(r => (r.Id, r.TenantId)).ToList();
            default:
                if (target.Code is null || !run.ByCode.TryGetValue(target.Code, out var source) || source.RetiredAt is not null)
                    return [];
                return (await _db.RolePermissions
                        .Where(rp => rp.PermissionId == source.Id)
                        .Select(rp => new { rp.RoleId, rp.TenantId })
                        .ToListAsync(ct))
                    .Select(r => (r.RoleId, r.TenantId)).ToList();
        }
    }

    private async Task ApplyRetirementsAsync(PermissionLifecycleRules rules, Run run, CancellationToken ct)
    {
        foreach (var retirement in rules.Retirements)
        {
            if (!run.ByCode.TryGetValue(retirement.Code, out var row))
                continue;
            if (retirement.Stage == RetirementStage.SoftRetired && row.RetiredAt is not null)
                continue;

            var verb = retirement.Stage == RetirementStage.SoftRetired ? "soft-retire" : "remove";
            var holders = await CountHoldersAsync(row.Id, ct);
            foreach (var (tenantId, count) in holders.Tenants)
            {
                _logger.LogWarning(
                    "Permission {Code} ({Verb}) is still held in tenant {TenantId}: {RoleGrants} role grants, {Overrides} user overrides.",
                    retirement.Code, verb, tenantId, count.RoleGrants, count.Overrides);
            }

            if (holders.Total > 0 && !retirement.DropGrants)
            {
                throw new PermissionRetirementRefusedException(
                    $"Refusing to {verb} permission '{retirement.Code}': {holders.RoleGrants} role grant(s) and " +
                    $"{holders.Overrides} user override(s) in {holders.Tenants.Count} tenant(s) would be lost. " +
                    "Declare a rename to keep them under another code, or set DropGrants on the retirement " +
                    "if losing this access is intended.");
            }

            if (retirement.Stage == RetirementStage.SoftRetired)
            {
                row.RetiredAt = run.Now;
                run.Report.SoftRetired++;
            }
            else
            {
                _db.Permissions.Remove(row); // grants and overrides go by cascade, which DropGrants allowed
                run.ByCode.Remove(retirement.Code);
                run.Report.Removed++;
            }

            Record(run, $"{verb}:{retirement.Code}", verb, retirement.Code, null, new
            {
                retirement.Reason, retirement.Date, retirement.DropGrants,
                holders.RoleGrants, holders.Overrides, tenants = holders.Tenants,
            });
        }
    }

    private async Task<Holders> CountHoldersAsync(Guid permissionId, CancellationToken ct)
    {
        var roleGrants = await _db.RolePermissions
            .Where(rp => rp.PermissionId == permissionId)
            .GroupBy(rp => rp.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var overrides = await _db.UserPermissionOverrides
            .Where(o => o.PermissionId == permissionId)
            .GroupBy(o => o.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var tenants = roleGrants.Select(r => r.TenantId).Union(overrides.Select(o => o.TenantId))
            .OrderBy(id => id)
            .ToDictionary(
                id => id.ToString(),
                id => new HolderCount(
                    roleGrants.FirstOrDefault(r => r.TenantId == id)?.Count ?? 0,
                    overrides.FirstOrDefault(o => o.TenantId == id)?.Count ?? 0));
        return new Holders(roleGrants.Sum(r => r.Count), overrides.Sum(o => o.Count), tenants);
    }

    private void Record(Run run, string key, string kind, string code, string? previousCode, object detail)
    {
        // A change that happens again (a code renamed back, retired a second time) keeps its first
        // ledger row and gets a second, time-stamped one.
        var changeKey = run.Ledger.Contains(key) ? $"{key}@{run.Now:O}" : key;
        run.Ledger.Add(changeKey);
        _db.PermissionChanges.Add(new PermissionChange
        {
            Id = Guid.NewGuid(),
            ChangeKey = changeKey,
            Kind = kind,
            Code = code,
            PreviousCode = previousCode,
            Detail = JsonSerializer.Serialize(detail),
            AppliedAt = run.Now,
        });
    }

    private sealed class Run(DateTimeOffset now)
    {
        public DateTimeOffset Now { get; } = now;
        public required Dictionary<string, Permission> ByCode { get; init; }
        public required HashSet<string> Ledger { get; init; }
        public PermissionSyncReport Report { get; } = new();
    }

    private sealed record HolderCount(int RoleGrants, int Overrides)
    {
    }

    private sealed record Holders(int RoleGrants, int Overrides, IReadOnlyDictionary<string, HolderCount> Tenants)
    {
        public int Total => RoleGrants + Overrides;
    }
}

public sealed class PermissionSyncReport
{
    public bool Skipped { get; init; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Restored { get; set; }
    public int Renamed { get; set; }
    public int DefaultGrantsAdded { get; set; }
    public int SoftRetired { get; set; }
    public int Removed { get; set; }

    public bool HasChanges =>
        Added + Updated + Restored + Renamed + DefaultGrantsAdded + SoftRetired + Removed > 0;
}

/// <summary>A retirement would delete or disable grants that the declaration did not agree to lose.</summary>
public sealed class PermissionRetirementRefusedException : InvalidOperationException
{
    public PermissionRetirementRefusedException(string message) : base(message)
    {
    }
}
