using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;

/// <summary>
/// Resolves monitoring capability enablement:
/// employee override → role/position/dept policy → tenant toggle → false (safe default).
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
    /// Resolves "the employee" for a capability/threshold lookup. Every caller (all Monitoring
    /// command/query handlers, verified by grep) passes a User.Id sourced from the tray device
    /// identity, not a real Employee.Id - a user may own more than one Employee row for a
    /// multi-company user, so this defers to the same deterministic "default employee for this
    /// user" resolution TenantDatabaseTicketStore uses to seed a session's active company
    /// (most-recent active PrimaryEmployment assignment), instead of picking an arbitrary row.
    /// Deliberately does NOT also try matching by Employee.Id: doing so would risk resolving the
    /// wrong person if a User.Id ever collided with an unrelated Employee.Id (both are Guids in
    /// the same tenant's id space), for a case that has no real caller today.
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

    private async Task<int> ResolveMinutesAsync(
        Guid tenantId, Guid userId, Guid? legalEntityId, CancellationToken ct)
    {
        var employee = await ResolveEmployeeAsync(tenantId, userId, legalEntityId, ct);
        if (employee is null)
            return MonitoringToggleResolution.DefaultIdleThresholdMinutes;

        // 1. Employee-level override
        var employeeOverride = await _db.EmployeeMonitoringOverrides
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.EmployeeId == employee.Id, ct);
        var employeeMinutes = employeeOverride?.IdleThresholdMinutes;

        // 2. Policy overrides: role > position > department
        Guid? departmentId = employee?.DepartmentId;
        Guid? positionId = employee is null
            ? null
            : await _db.PositionAssignments
                .AsNoTracking()
                .Where(a => a.EmployeeId == employee.Id
                    && a.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                    && a.AssignmentStatus == PositionAssignmentStatus.Active
                    && a.EffectiveFrom <= DateOnly.FromDateTime(DateTime.UtcNow)
                    && (a.EffectiveTo == null || a.EffectiveTo >= DateOnly.FromDateTime(DateTime.UtcNow)))
                .OrderByDescending(a => a.EffectiveFrom)
                .Select(a => (Guid?)a.PositionId)
                .FirstOrDefaultAsync(ct);
        Guid userIdForRoles = employee!.UserId;

        var roleIds = await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.TenantId == tenantId && ur.UserId == userIdForRoles)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var scopeIds = new List<Guid>();
        scopeIds.AddRange(roleIds);
        if (positionId.HasValue) scopeIds.Add(positionId.Value);
        if (departmentId.HasValue) scopeIds.Add(departmentId.Value);

        int? roleMinutes = null;
        int? positionMinutes = null;
        int? departmentMinutes = null;

        if (scopeIds.Count > 0)
        {
            var policies = await _db.MonitoringPolicyOverrides
                .AsNoTracking()
                .Where(p => p.TenantId == tenantId && scopeIds.Contains(p.ScopeId))
                .ToListAsync(ct);

            roleMinutes = policies
                .Where(p => p.ScopeType == "role" && roleIds.Contains(p.ScopeId))
                .Select(p => p.IdleThresholdMinutes)
                .FirstOrDefault(v => v.HasValue);

            if (positionId.HasValue)
            {
                positionMinutes = policies
                    .Where(p => p.ScopeType == "position" && p.ScopeId == positionId.Value)
                    .Select(p => p.IdleThresholdMinutes)
                    .FirstOrDefault(v => v.HasValue);
            }

            if (departmentId.HasValue)
            {
                departmentMinutes = policies
                    .Where(p => p.ScopeType == "department" && p.ScopeId == departmentId.Value)
                    .Select(p => p.IdleThresholdMinutes)
                    .FirstOrDefault(v => v.HasValue);
            }
        }

        // 3. Legal-entity default, then retained tenant fallback.
        var toggles = await _db.MonitoringFeatureToggles
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId
                && (t.LegalEntityId == employee.LegalEntityId || t.LegalEntityId == null))
            .OrderByDescending(t => t.LegalEntityId == employee.LegalEntityId)
            .FirstOrDefaultAsync(ct);
        var tenantMinutes = toggles?.IdleThresholdMinutes;

        return MonitoringToggleResolution.ResolveMinutes(
            employeeMinutes, roleMinutes, positionMinutes, departmentMinutes, tenantMinutes);
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

        // 1. Employee-level override
        var employeeOverride = await _db.EmployeeMonitoringOverrides
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.EmployeeId == employee.Id, ct);
        bool? employeeValue = employeeOverride is null
            ? null
            : GetCapability(employeeOverride, capability);

        // 2. Policy overrides: role > position > department
        // EmployeeId in Phase 1 may be UserId (tray identity) or a real Employee.Id.
        Guid? departmentId = employee?.DepartmentId;
        Guid? positionId = employee is null
            ? null
            : await _db.PositionAssignments
                .AsNoTracking()
                .Where(a => a.EmployeeId == employee.Id
                    && a.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                    && a.AssignmentStatus == PositionAssignmentStatus.Active
                    && a.EffectiveFrom <= DateOnly.FromDateTime(DateTime.UtcNow)
                    && (a.EffectiveTo == null || a.EffectiveTo >= DateOnly.FromDateTime(DateTime.UtcNow)))
                .OrderByDescending(a => a.EffectiveFrom)
                .Select(a => (Guid?)a.PositionId)
                .FirstOrDefaultAsync(ct);
        Guid userIdForRoles = employee!.UserId;

        var roleIds = await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.TenantId == tenantId && ur.UserId == userIdForRoles)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var scopeIds = new List<Guid>();
        scopeIds.AddRange(roleIds);
        if (positionId.HasValue) scopeIds.Add(positionId.Value);
        if (departmentId.HasValue) scopeIds.Add(departmentId.Value);

        bool? roleValue = null;
        bool? positionValue = null;
        bool? departmentValue = null;

        if (scopeIds.Count > 0)
        {
            var policies = await _db.MonitoringPolicyOverrides
                .AsNoTracking()
                .Where(p => p.TenantId == tenantId && scopeIds.Contains(p.ScopeId))
                .ToListAsync(ct);

            roleValue = policies
                .Where(p => p.ScopeType == "role" && roleIds.Contains(p.ScopeId))
                .Select(p => GetCapability(p, capability))
                .FirstOrDefault(v => v.HasValue);

            if (positionId.HasValue)
            {
                positionValue = policies
                    .Where(p => p.ScopeType == "position" && p.ScopeId == positionId.Value)
                    .Select(p => GetCapability(p, capability))
                    .FirstOrDefault(v => v.HasValue);
            }

            if (departmentId.HasValue)
            {
                departmentValue = policies
                    .Where(p => p.ScopeType == "department" && p.ScopeId == departmentId.Value)
                    .Select(p => GetCapability(p, capability))
                    .FirstOrDefault(v => v.HasValue);
            }
        }

        // 3. Legal-entity default, then retained tenant fallback.
        var toggles = await _db.MonitoringFeatureToggles
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId
                && (t.LegalEntityId == employee.LegalEntityId || t.LegalEntityId == null))
            .OrderByDescending(t => t.LegalEntityId == employee.LegalEntityId)
            .FirstOrDefaultAsync(ct);
        bool? tenantValue = toggles is null ? null : GetCapability(toggles, capability);

        return MonitoringToggleResolution.Resolve(
            employeeValue, roleValue, positionValue, departmentValue, tenantValue);
    }

    private static bool? GetCapability(EmployeeMonitoringOverride o, MonitoringCapability c) => c switch
    {
        MonitoringCapability.ActivityMonitoring => o.ActivityMonitoring,
        MonitoringCapability.ApplicationTracking => o.ApplicationTracking,
        MonitoringCapability.DocumentTracking => o.DocumentTracking,
        MonitoringCapability.CommunicationTracking => o.CommunicationTracking,
        MonitoringCapability.ScreenshotCapture => o.ScreenshotCapture,
        MonitoringCapability.AutoScreenshotCapture => o.AutoScreenshotCapture,
        MonitoringCapability.MeetingDetection => o.MeetingDetection,
        MonitoringCapability.DeviceTracking => o.DeviceTracking,
        MonitoringCapability.WorkLocationVerification => o.WorkLocationVerification,
        MonitoringCapability.IdentityVerification => o.IdentityVerification,
        MonitoringCapability.Biometric => o.Biometric,
        _ => null
    };

    private static bool? GetCapability(MonitoringPolicyOverride o, MonitoringCapability c) => c switch
    {
        MonitoringCapability.ActivityMonitoring => o.ActivityMonitoring,
        MonitoringCapability.ApplicationTracking => o.ApplicationTracking,
        MonitoringCapability.DocumentTracking => o.DocumentTracking,
        MonitoringCapability.CommunicationTracking => o.CommunicationTracking,
        MonitoringCapability.ScreenshotCapture => o.ScreenshotCapture,
        MonitoringCapability.AutoScreenshotCapture => o.AutoScreenshotCapture,
        MonitoringCapability.MeetingDetection => o.MeetingDetection,
        MonitoringCapability.DeviceTracking => o.DeviceTracking,
        MonitoringCapability.WorkLocationVerification => o.WorkLocationVerification,
        MonitoringCapability.IdentityVerification => o.IdentityVerification,
        MonitoringCapability.Biometric => o.Biometric,
        _ => null
    };

    private static bool GetCapability(MonitoringFeatureToggles t, MonitoringCapability c) => c switch
    {
        MonitoringCapability.ActivityMonitoring => t.ActivityMonitoring,
        MonitoringCapability.ApplicationTracking => t.ApplicationTracking,
        MonitoringCapability.DocumentTracking => t.DocumentTracking,
        MonitoringCapability.CommunicationTracking => t.CommunicationTracking,
        MonitoringCapability.ScreenshotCapture => t.ScreenshotCapture,
        MonitoringCapability.AutoScreenshotCapture => t.AutoScreenshotCapture,
        MonitoringCapability.MeetingDetection => t.MeetingDetection,
        MonitoringCapability.DeviceTracking => t.DeviceTracking,
        MonitoringCapability.WorkLocationVerification => t.WorkLocationVerification,
        MonitoringCapability.IdentityVerification => t.IdentityVerification,
        MonitoringCapability.Biometric => t.Biometric,
        _ => false
    };
}
