using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.UserAccounts.RepositoryInterfaces;
using ONEVO.Domain.Lookups;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.UserAccounts;

public sealed class EfUserAccountRepository : IUserAccountRepository
{
    // Provisioned tenants seed "Owner" (DefaultRoleSeeder); dev smoke tenants seed "Tenant Owner".
    private static readonly string[] OwnerRoleNames = ["Owner", "Tenant Owner"];

    private readonly ApplicationDbContext _db;

    public EfUserAccountRepository(ApplicationDbContext db) => _db = db;

    public Task<IReadOnlyList<UserAccountRow>> ListAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct = default)
        => LoadAsync(tenantId, null, now, ct);

    public async Task<UserAccountRow?> GetAsync(Guid tenantId, Guid userId, DateTimeOffset now, CancellationToken ct = default)
        => (await LoadAsync(tenantId, userId, now, ct)).FirstOrDefault();

    public async Task<IReadOnlyList<UserWebSessionRow>> ListActiveSessionsAsync(
        Guid tenantId, Guid userId, DateTimeOffset now, CancellationToken ct = default)
        => await _db.Sessions.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.UserId == userId && !s.IsRevoked && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastActivityAt)
            .Select(s => new UserWebSessionRow(s.Id, s.IpAddress, s.UserAgent, s.StartedAt, s.LastActivityAt, s.ExpiresAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UserTrayDeviceRow>> ListActiveDevicesAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default)
        => await _db.TrayDeviceRegistrations.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.UserId == userId && d.IsActive)
            .OrderByDescending(d => d.LastSeenAt ?? d.ActivatedAt)
            .Select(d => new UserTrayDeviceRow(d.Id, d.DeviceName, d.DeviceOs, d.ActivatedAt, d.LastSeenAt))
            .ToListAsync(ct);

    public async Task<bool> RevokeSessionAsync(Guid tenantId, Guid userId, Guid sessionId, CancellationToken ct = default)
        => await _db.Sessions
            .Where(s => s.TenantId == tenantId && s.UserId == userId && s.Id == sessionId && !s.IsRevoked)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsRevoked, true), ct) > 0;

    public async Task<bool> RevokeDeviceAsync(
        Guid tenantId, Guid userId, Guid deviceId, DateTimeOffset now, string reason, CancellationToken ct = default)
    {
        var deactivated = await _db.TrayDeviceRegistrations
            .Where(d => d.TenantId == tenantId && d.UserId == userId && d.Id == deviceId && d.IsActive)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.IsActive, false)
                .SetProperty(d => d.DeactivatedAt, now), ct);
        if (deactivated == 0)
            return false;

        await _db.TrayDeviceRefreshTokens
            .Where(t => t.DeviceRegistrationId == deviceId && !t.IsRevoked)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsRevoked, true)
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, reason), ct);
        return true;
    }

    public async Task<int> RevokeAllAccessAsync(
        Guid tenantId, Guid userId, DateTimeOffset now, string reason, CancellationToken ct = default)
    {
        var sessions = await _db.Sessions
            .Where(s => s.TenantId == tenantId && s.UserId == userId && !s.IsRevoked)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsRevoked, true), ct);

        await _db.RefreshTokens
            .Where(t => t.TenantId == tenantId && t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

        var deviceIds = await _db.TrayDeviceRegistrations
            .Where(d => d.TenantId == tenantId && d.UserId == userId && d.IsActive)
            .Select(d => d.Id)
            .ToListAsync(ct);

        if (deviceIds.Count > 0)
        {
            await _db.TrayDeviceRefreshTokens
                .Where(t => deviceIds.Contains(t.DeviceRegistrationId) && !t.IsRevoked)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.IsRevoked, true)
                    .SetProperty(t => t.RevokedAt, now)
                    .SetProperty(t => t.RevokedReason, reason), ct);

            await _db.TrayDeviceRegistrations
                .Where(d => deviceIds.Contains(d.Id) && d.IsActive)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.IsActive, false)
                    .SetProperty(d => d.DeactivatedAt, now), ct);
        }

        return sessions + deviceIds.Count;
    }

    private async Task<IReadOnlyList<UserAccountRow>> LoadAsync(
        Guid tenantId, Guid? onlyUserId, DateTimeOffset now, CancellationToken ct)
    {
        var users = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && !u.IsDeleted)
            .Where(u => onlyUserId == null || u.Id == onlyUserId)
            .Select(u => new
            {
                u.Id, u.FirstName, u.LastName, u.Email, u.IsActive,
                u.SuspendedAt, u.LastLoginAt, u.CreatedAt,
            })
            .ToListAsync(ct);
        if (users.Count == 0)
            return [];

        var userIds = users.Select(u => u.Id).ToList();

        // A user can hold one employee record per company; show the earliest live one.
        var employees = (await _db.Employees.AsNoTracking()
                .Where(e => e.TenantId == tenantId && userIds.Contains(e.UserId))
                .Select(e => new { e.Id, e.UserId, e.EmploymentStatusId, e.CreatedAt })
                .ToListAsync(ct))
            .GroupBy(e => e.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(e => e.EmploymentStatusId is EmploymentStatusIds.Terminated or EmploymentStatusIds.Resigned)
                    .ThenBy(e => e.CreatedAt)
                    .First());

        var roles = (await _db.UserRoles.AsNoTracking()
                .Where(ur => ur.TenantId == tenantId && userIds.Contains(ur.UserId)
                    && (ur.ExpiresAt == null || ur.ExpiresAt > now))
                .Select(ur => new { ur.UserId, ur.Role.Name, ur.Role.IsSystem })
                .ToListAsync(ct))
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var invitations = (await _db.InvitationTokens.AsNoTracking()
                .Where(i => i.TenantId == tenantId && userIds.Contains(i.UserId))
                .Select(i => new { i.UserId, i.CreatedAt, i.ExpiresAt, i.UsedAt, i.RevokedAt })
                .ToListAsync(ct))
            .GroupBy(i => i.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.CreatedAt).First());

        var sessionCounts = await _db.Sessions.AsNoTracking()
            .Where(s => s.TenantId == tenantId && userIds.Contains(s.UserId) && !s.IsRevoked && s.ExpiresAt > now)
            .GroupBy(s => s.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        var deviceCounts = await _db.TrayDeviceRegistrations.AsNoTracking()
            .Where(d => d.TenantId == tenantId && userIds.Contains(d.UserId) && d.IsActive)
            .GroupBy(d => d.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        return users.Select(u =>
        {
            employees.TryGetValue(u.Id, out var employee);
            var userRoles = roles.GetValueOrDefault(u.Id) ?? [];
            invitations.TryGetValue(u.Id, out var invitation);

            return new UserAccountRow(
                u.Id, u.FirstName, u.LastName, u.Email, u.IsActive, u.SuspendedAt, u.LastLoginAt, u.CreatedAt,
                employee?.Id,
                employee?.EmploymentStatusId,
                userRoles.Select(r => r.Name).Distinct().OrderBy(n => n).ToList(),
                userRoles.Any(r => r.IsSystem && OwnerRoleNames.Contains(r.Name, StringComparer.OrdinalIgnoreCase)),
                invitation?.ExpiresAt,
                invitation?.UsedAt,
                invitation?.RevokedAt,
                sessionCounts.GetValueOrDefault(u.Id),
                deviceCounts.GetValueOrDefault(u.Id));
        }).ToList();
    }
}
