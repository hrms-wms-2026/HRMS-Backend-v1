using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.TimeAttendance.Alerts;
using ONEVO.Domain.Features.TimeAttendance.Alerts;

namespace ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;

public sealed class EfAttendanceAlertRepository : IAttendanceAlertRepository
{
    private readonly ApplicationDbContext _db;

    public EfAttendanceAlertRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyDictionary<string, IReadOnlySet<DateOnly>>> ExcusedDatesAsync(
        Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await _db.AttendanceAlerts.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.EmployeeId == employeeId && a.WorkDate >= from && a.WorkDate <= to
                        && (a.Outcome == AttendanceAlertOutcomes.Excused || a.Outcome == AttendanceAlertOutcomes.ConvertedToLeave))
            .Select(a => new { a.SignalKey, a.WorkDate })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.SignalKey)
            .ToDictionary(g => g.Key, g => (IReadOnlySet<DateOnly>)g.Select(r => r.WorkDate).ToHashSet());
    }

    public Task<List<AttendanceAlert>> ListForEmployeeAsync(
        Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct) =>
        _db.AttendanceAlerts
            .Where(a => a.TenantId == tenantId && a.EmployeeId == employeeId && a.WorkDate >= from && a.WorkDate <= to)
            .ToListAsync(ct);

    public Task<AttendanceAlert?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct) =>
        _db.AttendanceAlerts.FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == id, ct);

    public Task<AttendanceAlert?> FindOpenAsync(
        Guid tenantId, Guid employeeId, DateOnly workDate, string signalKey, CancellationToken ct) =>
        _db.AttendanceAlerts.FirstOrDefaultAsync(a => a.TenantId == tenantId && a.EmployeeId == employeeId
            && a.WorkDate == workDate && a.SignalKey == signalKey && a.Status == AttendanceAlertStatuses.Open, ct);

    public void Add(AttendanceAlert alert) => _db.AttendanceAlerts.Add(alert);

    public void Remove(AttendanceAlert alert) => _db.AttendanceAlerts.Remove(alert);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
