using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;

/// <summary>
/// Resolves monitoring capability enablement:
/// employee → work mode → role → position → department → legal entity → tenant → false (safe default),
/// all read from monitoring_settings in one query per resolution (Improvements task 36).
/// Cached 2 minutes per tenant/employee/capability.
/// </summary>
public class MonitoringToggleResolverService : IMonitoringToggleResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    private readonly ApplicationDbContext _db;
    private readonly ICacheService _cache;

    public MonitoringToggleResolverService(ApplicationDbContext db, ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    /// <summary>
    /// Resolves "the employee" for a capability/threshold lookup. Every parameter named `userId`
    /// (or `employeeId` at call sites that still use that misleading name - see
    /// AttendanceTodayStateService/LocationRuleEvaluatorJob's own comments for two that
    /// originally got this wrong) must be a real User.Id, not an Employee.Id - a user may own
    /// more than one Employee row for a multi-company user, so this defers to the same
    /// deterministic "default employee for this user" resolution TenantDatabaseTicketStore uses
    /// to seed a session's active company (most-recent active PrimaryEmployment assignment),
    /// instead of picking an arbitrary row. Deliberately does NOT also try matching by
    /// Employee.Id: doing so would risk resolving the wrong person if a User.Id ever collided
    /// with an unrelated Employee.Id (both are Guids in the same tenant's id space). A caller
    /// holding a real Employee.Id and a legal entity must resolve the employee's own UserId
    /// first and pass the three-arg (tenantId, userId, legalEntityId) overload - the two-arg
    /// overload's null-legal-entity fallback only resolves an unambiguous single active employee
    /// for that user, which fails for any multi-company user.
    /// </summary>
    private async Task<Employee?> ResolveEmployeeAsync(
        Guid tenantId, Guid userId, Guid? legalEntityId, CancellationToken ct)
    {
        var query =
            from employee in _db.Employees.AsNoTracking()
            join status in _db.EmploymentStatuses.AsNoTracking()
                on employee.EmploymentStatusId equals status.Id
            where employee.TenantId == tenantId
                && employee.UserId == userId
                && status.Code == "active"
            select employee;

        if (legalEntityId.HasValue)
            return await query.SingleOrDefaultAsync(
                employee => employee.LegalEntityId == legalEntityId.Value, ct);

        // Legacy tray tokens have no company claim. Resolve only an unambiguous active employee.
        var candidates = await query.OrderBy(employee => employee.Id).Take(2).ToListAsync(ct);
        return candidates.Count == 1 ? candidates[0] : null;
    }

    public async Task<bool> IsEnabledAsync(
        Guid tenantId,
        Guid employeeId,
        MonitoringCapability capability,
        CancellationToken ct = default)
    {
        return await IsEnabledCoreAsync(tenantId, employeeId, null, capability, ct);
    }

    public Task<bool> IsEnabledAsync(
        Guid tenantId, Guid userId, Guid legalEntityId, MonitoringCapability capability,
        CancellationToken ct = default) =>
        IsEnabledCoreAsync(tenantId, userId, legalEntityId, capability, ct);

    private async Task<bool> IsEnabledCoreAsync(
        Guid tenantId, Guid userId, Guid? legalEntityId, MonitoringCapability capability,
        CancellationToken ct)
    {
        var cacheKey = $"tenant:{tenantId}:monitoring-toggle:user:{userId}:legal-entity:{legalEntityId}:{capability}";
        var cached = await _cache.GetAsync<bool?>(cacheKey, ct);
        if (cached.HasValue)
            return cached.Value;

        var resolved = await ResolveAsync(tenantId, userId, legalEntityId, capability, ct);
        await _cache.SetAsync(cacheKey, resolved, CacheTtl, ct);
        return resolved;
    }

    public async Task<bool> IsEnabledForEmployeeAsync(
        Guid tenantId, Guid employeeId, MonitoringCapability capability, CancellationToken ct = default)
    {
        // Distinct "employee:" key segment so it never collides with the User.Id-keyed entries above.
        var cacheKey = $"tenant:{tenantId}:monitoring-toggle:employee:{employeeId}:{capability}";
        var cached = await _cache.GetAsync<bool?>(cacheKey, ct);
        if (cached.HasValue)
            return cached.Value;

        // Looked up by Employee.Id directly (no active-status filter): employee-detail reads also
        // show history for employees who have since left.
        var employee = await _db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == employeeId, ct);
        var resolved = employee is not null && await ResolveForEmployeeAsync(tenantId, employee, capability, ct);
        await _cache.SetAsync(cacheKey, resolved, CacheTtl, ct);
        return resolved;
    }

    public async Task<int> GetIdleThresholdMinutesAsync(
        Guid tenantId,
        Guid employeeId,
        CancellationToken ct = default)
    {
        return await GetIdleThresholdMinutesCoreAsync(tenantId, employeeId, null, ct);
    }

    public Task<int> GetIdleThresholdMinutesAsync(
        Guid tenantId, Guid userId, Guid legalEntityId, CancellationToken ct = default) =>
        GetIdleThresholdMinutesCoreAsync(tenantId, userId, legalEntityId, ct);

    private async Task<int> GetIdleThresholdMinutesCoreAsync(
        Guid tenantId, Guid userId, Guid? legalEntityId, CancellationToken ct)
    {
        var cacheKey = $"tenant:{tenantId}:monitoring-toggle:user:{userId}:legal-entity:{legalEntityId}:idle-threshold-minutes";
        var cached = await _cache.GetAsync<int?>(cacheKey, ct);
        if (cached.HasValue)
            return cached.Value;

        var resolved = await ResolveMinutesAsync(tenantId, userId, legalEntityId, ct);
        await _cache.SetAsync(cacheKey, resolved, CacheTtl, ct);
        return resolved;
    }

    public async Task<int?> GetAllowedRadiusMetersAsync(
        Guid tenantId,
        Guid employeeId,
        CancellationToken ct = default)
    {
        return await GetAllowedRadiusMetersCoreAsync(tenantId, employeeId, null, ct);
    }

    public Task<int?> GetAllowedRadiusMetersAsync(
        Guid tenantId, Guid userId, Guid legalEntityId, CancellationToken ct = default) =>
        GetAllowedRadiusMetersCoreAsync(tenantId, userId, legalEntityId, ct);

    /// <summary>
    /// Wraps the resolved radius so a genuinely-null result ("no tier configures a radius") is
    /// itself a real, cacheable value - unlike <see cref="GetIdleThresholdMinutesCoreAsync"/>'s
    /// bare <c>int?</c> cache entry, a bare <c>int?</c> here can't distinguish "resolved to null"
    /// from "nothing cached yet", so a null result would never be served from cache and would
    /// re-run the full resolution chain (employee resolve, policy/role/position lookups, feature
    /// toggles) on every call - the common case today, since AllowedRadiusMeters has no seed/
    /// default value.
    /// </summary>
    private sealed record CachedRadiusMeters(int? Value);

    private async Task<int?> GetAllowedRadiusMetersCoreAsync(
        Guid tenantId, Guid userId, Guid? legalEntityId, CancellationToken ct)
    {
        var cacheKey = $"tenant:{tenantId}:monitoring-toggle:user:{userId}:legal-entity:{legalEntityId}:allowed-radius-meters";
        var cached = await _cache.GetAsync<CachedRadiusMeters>(cacheKey, ct);
        if (cached is not null)
            return cached.Value;

        var resolved = await ResolveRadiusMetersAsync(tenantId, userId, legalEntityId, ct);
        await _cache.SetAsync(cacheKey, new CachedRadiusMeters(resolved), CacheTtl, ct);
        return resolved;
    }

    private async Task<int> ResolveMinutesAsync(
        Guid tenantId, Guid userId, Guid? legalEntityId, CancellationToken ct)
    {
        var employee = await ResolveEmployeeAsync(tenantId, userId, legalEntityId, ct);
        if (employee is null)
            return MonitoringToggleResolution.DefaultIdleThresholdMinutes;

        var levels = await LoadLevelsAsync(tenantId, employee, ct);
        return MonitoringToggleResolution.ResolveMinutes(
            levels.Employee?.IdleThresholdMinutes,
            levels.WorkMode?.IdleThresholdMinutes,
            levels.FirstRole(s => s.IdleThresholdMinutes),
            levels.Position?.IdleThresholdMinutes,
            levels.Department?.IdleThresholdMinutes,
            levels.Base?.IdleThresholdMinutes);
    }

    private async Task<int?> ResolveRadiusMetersAsync(
        Guid tenantId, Guid userId, Guid? legalEntityId, CancellationToken ct)
    {
        var employee = await ResolveEmployeeAsync(tenantId, userId, legalEntityId, ct);
        if (employee is null)
            return null;

        var levels = await LoadLevelsAsync(tenantId, employee, ct);
        return MonitoringToggleResolution.ResolveRadiusMeters(
            levels.Employee?.AllowedRadiusMeters,
            levels.WorkMode?.AllowedRadiusMeters,
            levels.FirstRole(s => s.AllowedRadiusMeters),
            levels.Position?.AllowedRadiusMeters,
            levels.Department?.AllowedRadiusMeters,
            levels.Base?.AllowedRadiusMeters);
    }

    private async Task<bool> ResolveAsync(
        Guid tenantId,
        Guid userId,
        Guid? legalEntityId,
        MonitoringCapability capability,
        CancellationToken ct)
    {
        var employee = await ResolveEmployeeAsync(tenantId, userId, legalEntityId, ct);
        if (employee is null)
            return false;

        return await ResolveForEmployeeAsync(tenantId, employee, capability, ct);
    }

    private async Task<bool> ResolveForEmployeeAsync(
        Guid tenantId,
        Employee employee,
        MonitoringCapability capability,
        CancellationToken ct)
    {
        var levels = await LoadLevelsAsync(tenantId, employee, ct);
        return MonitoringToggleResolution.Resolve(
            GetCapability(levels.Employee, capability),
            GetCapability(levels.WorkMode, capability),
            levels.FirstRole(s => GetCapability(s, capability)),
            GetCapability(levels.Position, capability),
            GetCapability(levels.Department, capability),
            GetCapability(levels.Base, capability));
    }

    /// <summary>
    /// Every monitoring_settings row that applies to the employee, read in one query (task 36):
    /// their own row, work mode, roles, primary position, department, legal entity and the tenant
    /// fallback. Before task 36 this took one query per level across three tables.
    /// </summary>
    private async Task<SettingLevels> LoadLevelsAsync(Guid tenantId, Employee employee, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var positionId = await _db.PositionAssignments
            .AsNoTracking()
            .Where(a => a.EmployeeId == employee.Id
                && a.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && a.AssignmentStatus == PositionAssignmentStatus.Active
                && a.EffectiveFrom <= today
                && (a.EffectiveTo == null || a.EffectiveTo >= today))
            .OrderByDescending(a => a.EffectiveFrom)
            .Select(a => (Guid?)a.PositionId)
            .FirstOrDefaultAsync(ct);

        var roleIds = await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.TenantId == tenantId && ur.UserId == employee.UserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var scopeIds = new List<Guid> { employee.Id, tenantId };
        if (employee.LegalEntityId is Guid legalEntity) scopeIds.Add(legalEntity);
        scopeIds.AddRange(roleIds);
        if (employee.WorkModeId is Guid workModeId) scopeIds.Add(workModeId);
        if (positionId is Guid position) scopeIds.Add(position);
        if (employee.DepartmentId is Guid department) scopeIds.Add(department);

        var rows = await _db.MonitoringSettings
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && scopeIds.Contains(s.ScopeId))
            .ToListAsync(ct);

        MonitoringSetting? Row(string scope, Guid? id) =>
            id is Guid value ? rows.FirstOrDefault(r => r.ScopeType == scope && r.ScopeId == value) : null;

        return new SettingLevels(
            Row(MonitoringSettingScopes.Employee, employee.Id),
            Row(MonitoringSettingScopes.WorkMode, employee.WorkModeId),
            rows.Where(r => r.ScopeType == MonitoringSettingScopes.Role && roleIds.Contains(r.ScopeId))
                .OrderBy(r => r.ScopeId)
                .ToList(),
            Row(MonitoringSettingScopes.Position, positionId),
            Row(MonitoringSettingScopes.Department, employee.DepartmentId),
            // Legal-entity default, then the retained tenant fallback.
            Row(MonitoringSettingScopes.LegalEntity, employee.LegalEntityId)
                ?? Row(MonitoringSettingScopes.Tenant, tenantId));
    }

    private sealed record SettingLevels(
        MonitoringSetting? Employee,
        MonitoringSetting? WorkMode,
        IReadOnlyList<MonitoringSetting> Roles,
        MonitoringSetting? Position,
        MonitoringSetting? Department,
        MonitoringSetting? Base)
    {
        /// <summary>The first role row that sets the value; roles have no order among themselves.</summary>
        public T? FirstRole<T>(Func<MonitoringSetting, T?> value) where T : struct =>
            Roles.Select(value).FirstOrDefault(v => v.HasValue);
    }

    internal static bool? GetCapability(MonitoringSetting? s, MonitoringCapability c) => s is null ? null : c switch
    {
        MonitoringCapability.ActivityMonitoring => s.ActivityMonitoring,
        MonitoringCapability.ApplicationTracking => s.ApplicationTracking,
        MonitoringCapability.DocumentTracking => s.DocumentTracking,
        MonitoringCapability.CommunicationTracking => s.CommunicationTracking,
        MonitoringCapability.ScreenshotCapture => s.ScreenshotCapture,
        MonitoringCapability.AutoScreenshotCapture => s.AutoScreenshotCapture,
        MonitoringCapability.MeetingDetection => s.MeetingDetection,
        MonitoringCapability.DeviceTracking => s.DeviceTracking,
        MonitoringCapability.WorkLocationVerification => s.WorkLocationVerification,
        MonitoringCapability.IdentityVerification => s.IdentityVerification,
        MonitoringCapability.Biometric => s.Biometric,
        _ => null
    };

    public async Task<bool> IsPhotoRequiredAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default) =>
        await IsPhotoRequiredCoreAsync(tenantId, employeeId, null, ct);

    public Task<bool> IsPhotoRequiredAsync(
        Guid tenantId, Guid userId, Guid legalEntityId, CancellationToken ct = default) =>
        IsPhotoRequiredCoreAsync(tenantId, userId, legalEntityId, ct);

    private async Task<bool> IsPhotoRequiredCoreAsync(
        Guid tenantId, Guid userId, Guid? legalEntityId, CancellationToken ct)
    {
        var cacheKey = $"tenant:{tenantId}:monitoring-toggle:user:{userId}:legal-entity:{legalEntityId}:photo-required";
        var cached = await _cache.GetAsync<bool?>(cacheKey, ct);
        if (cached.HasValue)
            return cached.Value;

        var employee = await ResolveEmployeeAsync(tenantId, userId, legalEntityId, ct);
        var resolved = false;
        var employeeOverride = employee is null
            ? null
            : await EmployeePhotoRequiredAsync(tenantId, employee.Id, ct);
        if (employeeOverride is bool overridden)
        {
            resolved = overridden;
        }
        else if (employee?.WorkModeId is Guid workModeId)
        {
            resolved = await _db.TimeAttendanceWorkModes
                .AsNoTracking()
                .Where(w => w.Id == workModeId && w.TenantId == tenantId)
                .Select(w => w.PhotoRequired)
                .FirstOrDefaultAsync(ct);
        }

        await _cache.SetAsync(cacheKey, resolved, CacheTtl, ct);
        return resolved;
    }

    public async Task<bool?> GetPhotoRequiredOverrideAsync(
        Guid tenantId, Guid userId, Guid legalEntityId, CancellationToken ct = default)
    {
        var employee = await ResolveEmployeeAsync(tenantId, userId, legalEntityId, ct);
        if (employee is null)
            return null;
        return await EmployeePhotoRequiredAsync(tenantId, employee.Id, ct);
    }

    private Task<bool?> EmployeePhotoRequiredAsync(Guid tenantId, Guid employeeId, CancellationToken ct) =>
        _db.MonitoringSettings
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId
                && s.ScopeType == MonitoringSettingScopes.Employee
                && s.ScopeId == employeeId)
            .Select(s => s.PhotoRequired)
            .FirstOrDefaultAsync(ct);
}
