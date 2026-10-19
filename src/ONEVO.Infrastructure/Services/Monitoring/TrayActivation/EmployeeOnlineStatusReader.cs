using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.Options;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.Monitoring.TrayActivation;

public sealed class EmployeeOnlineStatusReader : IEmployeeOnlineStatusReader
{
    private readonly ApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly TrayPresenceOptions _options;
    private readonly ITrayConnectionPresence? _livePresence;
    private readonly IWebActivityPresence? _webActivity;

    public EmployeeOnlineStatusReader(
        ApplicationDbContext db,
        IDateTimeProvider clock,
        IOptions<TrayPresenceOptions> options,
        ITrayConnectionPresence? livePresence = null,
        IWebActivityPresence? webActivity = null)
    {
        _db = db;
        _clock = clock;
        _options = options.Value;
        _livePresence = livePresence;
        _webActivity = webActivity;
    }

    public async Task<IReadOnlySet<Guid>> GetOnlineEmployeeIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct)
    {
        if (employeeIds.Count == 0) return new HashSet<Guid>();

        // Presence is tracked per user, so map the employees to their users first.
        var ids = employeeIds.Distinct().ToList();
        var users = await _db.Employees
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && ids.Contains(e.Id) && e.UserId != Guid.Empty)
            .Select(e => new { e.Id, e.UserId })
            .ToListAsync(ct);
        if (users.Count == 0) return new HashSet<Guid>();

        var userIds = users.Select(u => u.UserId).Distinct().ToList();
        var cutoff = _clock.UtcNow.AddSeconds(-_options.GracePeriodSeconds);
        var recentlySeen = await _db.TrayDeviceRegistrations
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.IsActive && userIds.Contains(d.UserId)
                && d.LastSeenAt != null && d.LastSeenAt > cutoff)
            .Select(d => d.UserId)
            .Distinct()
            .ToListAsync(ct);
        var onlineUsers = recentlySeen.ToHashSet();

        return users
            .Where(u => onlineUsers.Contains(u.UserId)
                || _livePresence?.IsConnected(tenantId, u.UserId) == true
                || _webActivity?.LastSeenAt(tenantId, u.UserId) > cutoff)
            .Select(u => u.Id)
            .ToHashSet();
    }
}
