using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
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

    public async Task<bool> IsEnabledAsync(
        Guid tenantId,
        Guid employeeId,
        MonitoringCapability capability,
        CancellationToken ct = default)
    {
        var cacheKey = $"tenant:{tenantId}:monitoring-toggle:employee:{employeeId}:{capability}";
        var cached = await _cache.GetAsync<bool?>(cacheKey, ct);
        if (cached.HasValue)
            return cached.Value;

        var resolved = await ResolveAsync(tenantId, employeeId, capability, ct);
        await _cache.SetAsync(cacheKey, resolved, CacheTtl, ct);
        return resolved;
    }

    public async Task<int> GetIdleThresholdMinutesAsync(
        Guid tenantId,
        Guid employeeId,
        CancellationToken ct = default)
    {
        var cacheKey = $"tenant:{tenantId}:monitoring-toggle:employee:{employeeId}:idle-threshold-minutes";
        var cached = await _cache.GetAsync<int?>(cacheKey, ct);
        if (cached.HasValue)
            return cached.Value;

        var resolved = await ResolveMinutesAsync(tenantId, employeeId, ct);
        await _cache.SetAsync(cacheKey, resolved, CacheTtl, ct);
        return resolved;
    }

    private async Task<int> ResolveMinutesAsync(Guid tenantId, Guid employeeId, CancellationToken ct)
    {
        // 1. Employee-level override
        var employeeOverride = await _db.EmployeeMonitoringOverrides
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.EmployeeId == employeeId, ct);
        var employeeMinutes = employeeOverride?.IdleThresholdMinutes;

        // 2. Policy overrides: role > position > department
        var employee = await _db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.TenantId == tenantId && (e.UserId == employeeId || e.Id == employeeId),
                ct);

        Guid? departmentId = employee?.DepartmentId;
        Guid? positionId = null; // TODO: resolve via position_assignments once it exists
        Guid userIdForRoles = employee?.UserId ?? employeeId;

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

        // 3. Tenant-level toggle row (null row or null column → fall through to hardcoded default)
        var toggles = await _db.MonitoringFeatureToggles
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
        var tenantMinutes = toggles?.IdleThresholdMinutes;

        return MonitoringToggleResolution.ResolveMinutes(
            employeeMinutes, roleMinutes, positionMinutes, departmentMinutes, tenantMinutes);
    }

    private async Task<bool> ResolveAsync(
        Guid tenantId,
        Guid employeeId,
        MonitoringCapability capability,
        CancellationToken ct)
    {
        // 1. Employee-level override
        var employeeOverride = await _db.EmployeeMonitoringOverrides
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.EmployeeId == employeeId, ct);
        bool? employeeValue = employeeOverride is null
            ? null
            : GetCapability(employeeOverride, capability);

        // 2. Policy overrides: role > position > department
        // EmployeeId in Phase 1 may be UserId (tray identity). Prefer Employee.UserId match,
        // then fall back to Employee.Id match for true CoreHR employee ids.
        var employee = await _db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.TenantId == tenantId && (e.UserId == employeeId || e.Id == employeeId),
                ct);

        Guid? departmentId = employee?.DepartmentId;
        Guid? positionId = null; // TODO: resolve via position_assignments once it exists
        Guid userIdForRoles = employee?.UserId ?? employeeId;

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

        // 3. Tenant-level toggles (null row → false safe default)
        var toggles = await _db.MonitoringFeatureToggles
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
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
