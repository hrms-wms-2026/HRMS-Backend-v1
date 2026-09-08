using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Settings.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Settings.Mappers;
using ONEVO.Application.Features.Monitoring.Settings.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.Monitoring.Settings;

public sealed class MonitoringPolicyConfigurationService : IMonitoringPolicyConfigurationService
{
    private readonly ApplicationDbContext _db;
    private readonly ICacheService _cache;

    public MonitoringPolicyConfigurationService(ApplicationDbContext db, ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<MonitoringPolicyConfigurationResponse> GetAsync(
        Guid tenantId, Guid? legalEntityId, CancellationToken ct = default)
    {
        var company = await _db.MonitoringFeatureToggles
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                && (x.LegalEntityId == legalEntityId || x.LegalEntityId == null))
            .OrderByDescending(x => x.LegalEntityId == legalEntityId)
            .FirstOrDefaultAsync(ct);
        var overrides = await _db.MonitoringPolicyOverrides
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.ScopeType)
            .ThenBy(x => x.ScopeId)
            .ToListAsync(ct);

        var visibleOverrides = new List<MonitoringPolicyOverride>(overrides.Count);
        foreach (var item in overrides)
        {
            if (item.ScopeType == "role" || await TargetExistsAsync(tenantId, item.ScopeType, item.ScopeId, legalEntityId, ct))
                visibleOverrides.Add(item);
        }

        var overrideResponses = new List<MonitoringPolicyOverrideResponse>(visibleOverrides.Count);
        foreach (var item in visibleOverrides)
        {
            overrideResponses.Add(ToResponse(item, await ResolveTargetNameAsync(item.ScopeType, item.ScopeId, ct)));
        }

        return new MonitoringPolicyConfigurationResponse(
            MonitoringFeatureTogglesMapper.ToResponse(company),
            overrideResponses,
            HasActiveCompanyContext: legalEntityId.HasValue);
    }

    public async Task<Result<MonitoringPolicyOverrideResponse>> UpsertOverrideAsync(
        Guid tenantId,
        Guid actorId,
        string scopeType,
        Guid scopeId,
        Guid? legalEntityId,
        MonitoringPolicyOverrideRequest request,
        CancellationToken ct = default)
    {
        var normalizedScope = scopeType.Trim().ToLowerInvariant();
        if (normalizedScope is not ("department" or "position" or "role"))
            return Result<MonitoringPolicyOverrideResponse>.UnprocessableEntity("Monitoring override scope is not supported.");
        if (scopeId == Guid.Empty)
            return Result<MonitoringPolicyOverrideResponse>.UnprocessableEntity("A valid monitoring override target is required.");
        if (request.IdleThresholdMinutes is <= 0 or > 1440)
            return Result<MonitoringPolicyOverrideResponse>.UnprocessableEntity("Activity threshold must be between 1 and 1440 minutes.");
        if (!await TargetExistsAsync(tenantId, normalizedScope, scopeId, legalEntityId, ct))
            return Result<MonitoringPolicyOverrideResponse>.NotFound("The selected monitoring override target was not found or is inactive.");

        var now = DateTimeOffset.UtcNow;
        var entity = await _db.MonitoringPolicyOverrides
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ScopeType == normalizedScope && x.ScopeId == scopeId, ct);
        if (entity is null)
        {
            entity = new MonitoringPolicyOverride
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ScopeType = normalizedScope,
                ScopeId = scopeId,
                SetById = actorId,
                CreatedAt = now
            };
            _db.MonitoringPolicyOverrides.Add(entity);
        }

        entity.ActivityMonitoring = request.ActivityMonitoring;
        entity.ApplicationTracking = request.ApplicationTracking;
        entity.DocumentTracking = request.DocumentTracking;
        entity.CommunicationTracking = request.CommunicationTracking;
        entity.ScreenshotCapture = request.ScreenshotCapture;
        entity.AutoScreenshotCapture = request.AutoScreenshotCapture;
        entity.MeetingDetection = request.MeetingDetection;
        entity.DeviceTracking = request.DeviceTracking;
        entity.WorkLocationVerification = request.WorkLocationVerification;
        entity.IdentityVerification = request.IdentityVerification;
        entity.Biometric = request.Biometric;
        entity.IdleThresholdMinutes = request.IdleThresholdMinutes;
        entity.OverrideReason = request.OverrideReason?.Trim() ?? string.Empty;
        entity.SetById = actorId;
        entity.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
        await _cache.RemoveByPrefixAsync($"tenant:{tenantId}:monitoring-toggle:", ct);
        return Result<MonitoringPolicyOverrideResponse>.Success(
            ToResponse(entity, await ResolveTargetNameAsync(entity.ScopeType, entity.ScopeId, ct)));
    }

    public async Task<Result> DeleteOverrideAsync(
        Guid tenantId, string scopeType, Guid scopeId, Guid? legalEntityId, CancellationToken ct = default)
    {
        var normalizedScope = scopeType.Trim().ToLowerInvariant();
        var entity = await _db.MonitoringPolicyOverrides
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ScopeType == normalizedScope && x.ScopeId == scopeId, ct);
        if (entity is null)
            return Result.NotFound("The monitoring override was not found.");

        // Same legal-entity guard as upsert: a department/position override belonging to a
        // different company than the caller's current active one is treated as not found, not
        // deleted. Role overrides are tenant-wide and unaffected.
        if (normalizedScope != "role" && !await TargetExistsAsync(tenantId, normalizedScope, scopeId, legalEntityId, ct))
            return Result.NotFound("The monitoring override was not found.");

        _db.MonitoringPolicyOverrides.Remove(entity);
        await _db.SaveChangesAsync(ct);
        await _cache.RemoveByPrefixAsync($"tenant:{tenantId}:monitoring-toggle:", ct);
        return Result.Success();
    }

    // Department/position targets must also belong to the caller's current active legal entity
    // (company). legalEntityId is server-derived (ICurrentUser.LegalEntityId, sourced from
    // TenantDatabaseTicketStore.RetrieveAsync's Session.ActiveEmployeeId -> Employee.LegalEntityId
    // claim) and is never accepted from the request - accepting one from the body/route would
    // repeat the tenantId-in-body anti-pattern this controller otherwise avoids. A null
    // legalEntityId (no active company context) fails closed for these two scopes. Role scope is
    // tenant-wide by design (roles are not legal-entity-scoped in Phase 1) and unaffected.
    private async Task<bool> TargetExistsAsync(Guid tenantId, string scopeType, Guid scopeId, Guid? legalEntityId, CancellationToken ct) => scopeType switch
    {
        "department" => legalEntityId is Guid deptLegalEntityId &&
            await _db.Departments.AnyAsync(x => x.Id == scopeId && x.TenantId == tenantId && x.IsActive && x.LegalEntityId == deptLegalEntityId, ct),
        "position" => legalEntityId is Guid posLegalEntityId &&
            await _db.Positions.AnyAsync(x => x.Id == scopeId && x.TenantId == tenantId && x.IsActive && x.LegalEntityId == posLegalEntityId, ct),
        "role" => await _db.Roles.AnyAsync(x => x.Id == scopeId && x.TenantId == tenantId, ct),
        _ => false
    };

    private async Task<string> ResolveTargetNameAsync(string scopeType, Guid scopeId, CancellationToken ct) => scopeType switch
    {
        "department" => await _db.Departments.Where(x => x.Id == scopeId).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "Department",
        "position" => await _db.Positions.Where(x => x.Id == scopeId).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "Position",
        "role" => await _db.Roles.Where(x => x.Id == scopeId).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "Role",
        _ => "Monitoring override"
    };

    private static MonitoringPolicyOverrideResponse ToResponse(MonitoringPolicyOverride x, string targetName) => new(
        x.Id, x.ScopeType, x.ScopeId, targetName, x.ActivityMonitoring, x.ApplicationTracking,
        x.DocumentTracking, x.CommunicationTracking, x.ScreenshotCapture,
        x.AutoScreenshotCapture, x.MeetingDetection, x.DeviceTracking,
        x.WorkLocationVerification, x.IdentityVerification, x.Biometric,
        x.IdleThresholdMinutes, x.OverrideReason, x.UpdatedAt);
}
