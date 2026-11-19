using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.TimeAttendance.Alerts;
using ONEVO.Domain.Features.TimeAttendance.Alerts;

namespace ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;

public sealed class EfAttendanceAlertDeductionRepository(ApplicationDbContext db) : IAttendanceAlertDeductionRepository
{
    public Task<AttendanceAlertDeduction?> GetByAlertAsync(Guid tenantId, Guid alertId, CancellationToken ct) =>
        db.AttendanceAlertDeductions.FirstOrDefaultAsync(d => d.TenantId == tenantId && d.AlertId == alertId, ct);

    public async Task<IReadOnlyDictionary<Guid, AttendanceAlertDeduction>> ListByAlertsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> alertIds, CancellationToken ct)
    {
        if (alertIds.Count == 0)
            return new Dictionary<Guid, AttendanceAlertDeduction>();
        var ids = alertIds.ToList();
        return await db.AttendanceAlertDeductions.AsNoTracking()
            .Where(d => d.TenantId == tenantId && ids.Contains(d.AlertId))
            .ToDictionaryAsync(d => d.AlertId, ct);
    }

    public void Add(AttendanceAlertDeduction deduction) => db.AttendanceAlertDeductions.Add(deduction);
}
