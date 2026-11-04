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

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlySet<DateOnly>>>> ExcusedDatesForEmployeesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (employeeIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlySet<DateOnly>>>();

        var rows = await _db.AttendanceAlerts.AsNoTracking()
            .Where(a => a.TenantId == tenantId && employeeIds.Contains(a.EmployeeId) && a.WorkDate >= from && a.WorkDate <= to
                        && (a.Outcome == AttendanceAlertOutcomes.Excused || a.Outcome == AttendanceAlertOutcomes.ConvertedToLeave))
            .Select(a => new { a.EmployeeId, a.SignalKey, a.WorkDate })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.EmployeeId)
            .ToDictionary(
                e => e.Key,
                e => (IReadOnlyDictionary<string, IReadOnlySet<DateOnly>>)e.GroupBy(r => r.SignalKey)
                    .ToDictionary(g => g.Key, g => (IReadOnlySet<DateOnly>)g.Select(r => r.WorkDate).ToHashSet()));
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

    public async Task<IReadOnlyList<Guid>> ListOpenEmployeeIdsAsync(Guid tenantId, Guid legalEntityId, CancellationToken ct) =>
        await _db.AttendanceAlerts.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.LegalEntityId == legalEntityId && a.Status == AttendanceAlertStatuses.Open)
            .Select(a => a.EmployeeId).Distinct()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AttendanceAlert>> ListOpenAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> employeeIds, int take, CancellationToken ct) =>
        await _db.AttendanceAlerts.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.LegalEntityId == legalEntityId && a.Status == AttendanceAlertStatuses.Open
                        && employeeIds.Contains(a.EmployeeId))
            .OrderByDescending(a => a.WorkDate).ThenBy(a => a.Id)
            .Take(take)
            .ToListAsync(ct);

    public void Add(AttendanceAlert alert) => _db.AttendanceAlerts.Add(alert);

    public void Remove(AttendanceAlert alert) => _db.AttendanceAlerts.Remove(alert);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
