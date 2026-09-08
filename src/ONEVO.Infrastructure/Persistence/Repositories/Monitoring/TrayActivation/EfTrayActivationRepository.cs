using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Enums;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.TrayActivation;

public class EfTrayActivationRepository : ITrayActivationRepository
{
    private readonly ApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public EfTrayActivationRepository(ApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<int> CountRecentCodesForUserAsync(
        Guid userId, Guid tenantId, DateTimeOffset since, CancellationToken ct)
    {
        return await _db.TrayActivationCodes
            .CountAsync(c => c.UserId == userId && c.TenantId == tenantId && c.CreatedAt >= since, ct);
    }

    public async Task AddActivationCodeAsync(TrayActivationCode code, CancellationToken ct)
    {
        await _db.TrayActivationCodes.AddAsync(code, ct);
    }

    public async Task<TrayActivationCode?> FindActiveCodeByHashAsync(string codeHash, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        return await _db.TrayActivationCodes
            .FromSqlInterpolated($"SELECT * FROM tray_activation_codes WHERE code_hash = {codeHash} AND used_at IS NULL AND expires_at > {now} FOR UPDATE")
            .AsTracking()
            .FirstOrDefaultAsync(ct);
    }

    public async Task MarkCodeUsedAsync(TrayActivationCode code, CancellationToken ct)
    {
        code.UsedAt = _clock.UtcNow;
        await Task.CompletedTask;
    }

    public async Task RevokeActiveRegistrationsForIdentityAsync(
        Guid tenantId,
        Guid userId,
        string deviceFingerprint,
        string reason,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var registrationIds = await _db.TrayDeviceRegistrations
            .Where(d => d.TenantId == tenantId
                && d.UserId == userId
                && d.DeviceFingerprint == deviceFingerprint
                && d.IsActive)
            .Select(d => d.Id)
            .ToListAsync(ct);

        if (registrationIds.Count == 0)
            return;

        await _db.TrayDeviceRefreshTokens
            .Where(t => registrationIds.Contains(t.DeviceRegistrationId) && !t.IsRevoked)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsRevoked, true)
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, reason), ct);

        await _db.TrayDeviceRegistrations
            .Where(d => registrationIds.Contains(d.Id) && d.IsActive)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.IsActive, false)
                .SetProperty(d => d.DeactivatedAt, now), ct);
    }

    public async Task<int> CountRecentDeviceAuthorizationRequestsAsync(
        string deviceFingerprintHash, DateTimeOffset since, CancellationToken ct)
    {
        return await _db.TrayDeviceAuthorizations
            .CountAsync(a => a.DeviceFingerprintHash == deviceFingerprintHash && a.CreatedAt >= since, ct);
    }

    public async Task AddDeviceAuthorizationAsync(
        TrayDeviceAuthorization authorization, CancellationToken ct)
    {
        await _db.TrayDeviceAuthorizations.AddAsync(authorization, ct);
    }

    public async Task<TrayDeviceAuthorization?> FindDeviceAuthorizationForApprovalAsync(
        Guid requestId, string userCodeHash, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        return await _db.TrayDeviceAuthorizations
            .FirstOrDefaultAsync(a => a.Id == requestId
                && a.UserCodeHash == userCodeHash
                && a.Status == DeviceAuthorizationStatus.Pending
                && a.ExpiresAt > now, ct);
    }

    public async Task<TrayDeviceAuthorization?> LockDeviceAuthorizationForPollAsync(
        string deviceCodeHash, CancellationToken ct)
    {
        return await _db.TrayDeviceAuthorizations
            .FromSqlInterpolated($"SELECT * FROM tray_device_authorizations WHERE device_code_hash = {deviceCodeHash} FOR UPDATE")
            .AsTracking()
            .FirstOrDefaultAsync(ct);
    }

    public async Task AddDeviceRegistrationAsync(TrayDeviceRegistration device, CancellationToken ct)
    {
        await _db.TrayDeviceRegistrations.AddAsync(device, ct);
    }

    public async Task AddRefreshTokenAsync(TrayDeviceRefreshToken token, CancellationToken ct)
    {
        await _db.TrayDeviceRefreshTokens.AddAsync(token, ct);
    }

    public async Task<TrayDeviceRefreshToken?> FindActiveRefreshTokenAsync(string tokenHash, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        return await _db.TrayDeviceRefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash
                && !t.IsRevoked
                && t.ExpiresAt > now, ct);
    }

    public async Task RevokeRefreshTokenAsync(TrayDeviceRefreshToken token, string reason, CancellationToken ct)
    {
        token.IsRevoked = true;
        token.RevokedAt = _clock.UtcNow;
        token.RevokedReason = reason;
        await Task.CompletedTask;
    }

    public async Task RevokeAllRefreshTokensForDeviceAsync(
        Guid deviceRegistrationId, string reason, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        await _db.TrayDeviceRefreshTokens
            .Where(t => t.DeviceRegistrationId == deviceRegistrationId && !t.IsRevoked)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsRevoked, true)
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, reason), ct);
    }

    public async Task<TrayDeviceRegistration?> FindActiveDeviceAsync(
        Guid deviceRegistrationId, Guid tenantId, CancellationToken ct)
    {
        return await _db.TrayDeviceRegistrations
            .FirstOrDefaultAsync(d => d.Id == deviceRegistrationId
                && d.TenantId == tenantId
                && d.IsActive, ct);
    }

    public async Task<TrayDeviceRegistration?> FindLatestActiveDeviceForUserAsync(
        Guid userId, Guid tenantId, CancellationToken ct)
    {
        return await _db.TrayDeviceRegistrations
            .Where(d => d.UserId == userId
                && d.TenantId == tenantId
                && d.IsActive
                && d.LastSeenAt != null)
            .OrderByDescending(d => d.LastSeenAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task UpdateDeviceLastSeenAsync(
        Guid deviceRegistrationId, DateTimeOffset lastSeenAt, CancellationToken ct)
    {
        await _db.TrayDeviceRegistrations
            .Where(d => d.Id == deviceRegistrationId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastSeenAt, lastSeenAt), ct);
    }

    public async Task DeactivateDeviceAsync(
        Guid deviceRegistrationId, DateTimeOffset deactivatedAt, CancellationToken ct)
    {
        await _db.TrayDeviceRegistrations
            .Where(d => d.Id == deviceRegistrationId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.IsActive, false)
                .SetProperty(d => d.DeactivatedAt, deactivatedAt), ct);
    }

    public async Task<TrayEmployeeProfile?> FindEmployeeProfileAsync(
        Guid userId, Guid tenantId, Guid? legalEntityId, CancellationToken ct)
    {
        var query =
            from employee in _db.Employees.AsNoTracking()
            join status in _db.EmploymentStatuses.AsNoTracking()
                on employee.EmploymentStatusId equals status.Id
            where employee.UserId == userId
                && employee.TenantId == tenantId
                && status.Code == "active"
            select employee;

        if (legalEntityId.HasValue)
            query = query.Where(employee => employee.LegalEntityId == legalEntityId.Value);

        var profiles = await (
                from employee in query
                join department in _db.Departments.AsNoTracking()
                    on employee.DepartmentId equals department.Id into departments
                from department in departments.DefaultIfEmpty()
                join workMode in _db.WorkModes.AsNoTracking()
                    on employee.WorkModeId equals workMode.Id into workModes
                from workMode in workModes.DefaultIfEmpty()
                join office in _db.LegalEntities.AsNoTracking()
                    on employee.LegalEntityId equals office.Id into offices
                from office in offices.DefaultIfEmpty()
                orderby employee.Id
                select new TrayEmployeeProfile(
                    employee.FirstName,
                    employee.LastName,
                    employee.Email,
                    employee.EmployeeNumber,
                    department != null ? department.Name : null,
                    workMode != null ? workMode.Label : null,
                    office != null ? office.Name : null)
            )
            .Take(2)
            .ToListAsync(ct);

        // A legacy device without company context is allowed to display an employee profile only
        // when the active employee is unambiguous. Never select a default row arbitrarily.
        return profiles.Count == 1 ? profiles[0] : null;
    }
}
