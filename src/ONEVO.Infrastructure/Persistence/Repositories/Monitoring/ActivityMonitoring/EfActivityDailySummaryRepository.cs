using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.ActivityMonitoring;

public class EfActivityDailySummaryRepository : IActivityDailySummaryRepository
{
    private readonly ApplicationDbContext _db;

    public EfActivityDailySummaryRepository(ApplicationDbContext db) => _db = db;

    public async Task<ActivityDailySummary?> GetAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly date,
        CancellationToken ct)
    {
        return await _db.ActivityDailySummaries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.TenantId == tenantId && s.EmployeeId == employeeId && s.Date == date,
                ct);
    }

    public async Task<IReadOnlyList<ActivityDailySummary>> GetRangeAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        return await _db.ActivityDailySummaries
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId
                        && s.EmployeeId == employeeId
                        && s.Date >= from
                        && s.Date <= to)
            .OrderBy(s => s.Date)
            .ToListAsync(ct);
    }

    public async Task UpsertAsync(ActivityDailySummary summary, CancellationToken ct)
    {
        var existing = await _db.ActivityDailySummaries
            .FirstOrDefaultAsync(
                s => s.TenantId == summary.TenantId
                     && s.EmployeeId == summary.EmployeeId
                     && s.Date == summary.Date,
                ct);

        if (existing is null)
        {
            await _db.ActivityDailySummaries.AddAsync(summary, ct);
            return;
        }

        existing.TotalActiveMinutes = summary.TotalActiveMinutes;
        existing.TotalIdleMinutes = summary.TotalIdleMinutes;
        existing.TotalMeetingMinutes = summary.TotalMeetingMinutes;
        existing.ActivePercentage = summary.ActivePercentage;
        existing.ProductiveAppMinutes = summary.ProductiveAppMinutes;
        existing.PersonalAppMinutes = summary.PersonalAppMinutes;
        existing.UnknownAppMinutes = summary.UnknownAppMinutes;
        existing.FocusMinutes = summary.FocusMinutes;
        existing.ActivityScore = summary.ActivityScore;
        existing.DataCoveragePercentage = summary.DataCoveragePercentage;
        existing.TopAppsJson = summary.TopAppsJson;
        existing.IntensityAvg = summary.IntensityAvg;
        existing.KeyboardTotal = summary.KeyboardTotal;
        existing.MouseTotal = summary.MouseTotal;
        existing.DocumentTimeMinutes = summary.DocumentTimeMinutes;
        existing.DeepFocusSessionsCount = summary.DeepFocusSessionsCount;
        existing.DataSource = summary.DataSource;
        existing.UpdatedAt = summary.UpdatedAt;
    }
}
