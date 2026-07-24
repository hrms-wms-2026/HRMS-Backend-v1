using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.ActivityMonitoring;

public sealed class EfActivityMonitoringRepository : IActivityMonitoringRepository
{
    private readonly ApplicationDbContext _db;
    public EfActivityMonitoringRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<RawBufferItem>> GetPendingRawBatchAsync(int maxRows, CancellationToken ct) =>
        await _db.ActivityRawBuffer
            .OrderBy(b => b.ReceivedAt)
            .Take(maxRows)
            .Select(b => new RawBufferItem(b.Id, b.TenantId, b.AgentDeviceId, b.ReceivedAt, b.PayloadJson))
            .ToListAsync(ct);

    public async Task BulkInsertSnapshotsAsync(IEnumerable<ActivitySnapshot> snapshots, CancellationToken ct) =>
        await _db.ActivitySnapshots.AddRangeAsync(snapshots, ct);

    public async Task BulkInsertApplicationUsageAsync(IEnumerable<ApplicationUsage> usage, CancellationToken ct) =>
        await _db.ApplicationUsage.AddRangeAsync(usage, ct);

    public async Task BulkInsertMeetingSessionsAsync(IEnumerable<MeetingSession> sessions, CancellationToken ct) =>
        await _db.MeetingSessions.AddRangeAsync(sessions, ct);

    public async Task UpsertDeviceTrackingAsync(DeviceTracking tracking, CancellationToken ct)
    {
        var existing = await _db.DeviceTracking
            .FirstOrDefaultAsync(d => d.TenantId == tracking.TenantId
                                      && d.EmployeeId == tracking.EmployeeId
                                      && d.Date == tracking.Date, ct);
        if (existing is null)
            await _db.DeviceTracking.AddAsync(tracking, ct);
        else
        {
            existing.LaptopActiveMinutes += tracking.LaptopActiveMinutes;
            existing.LaptopPercentage = tracking.LaptopPercentage;
        }
    }

    public async Task DeleteRawBufferRowsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var idList = ids.ToList();
        await _db.ActivityRawBuffer
            .Where(b => idList.Contains(b.Id))
            .ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<ActivitySnapshot>> GetSnapshotsForDayAsync(
        Guid employeeId, DateOnly date, CancellationToken ct)
    {
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        return await _db.ActivitySnapshots
            .Where(s => s.EmployeeId == employeeId
                        && s.CapturedAt >= start && s.CapturedAt <= end)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ApplicationUsage>> GetAppUsageForDayAsync(
        Guid employeeId, DateOnly date, CancellationToken ct) =>
        await _db.ApplicationUsage
            .Where(u => u.EmployeeId == employeeId && u.Date == date)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MeetingSession>> GetMeetingsForDayAsync(
        Guid employeeId, DateOnly date, CancellationToken ct)
    {
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        return await _db.MeetingSessions
            .Where(m => m.EmployeeId == employeeId
                        && m.MeetingStart >= start && m.MeetingStart <= end)
            .ToListAsync(ct);
    }

    public async Task UpsertDailySummaryAsync(ActivityDailySummary summary, CancellationToken ct)
    {
        var existing = await _db.ActivityDailySummaries
            .FirstOrDefaultAsync(s => s.TenantId == summary.TenantId
                                      && s.EmployeeId == summary.EmployeeId
                                      && s.Date == summary.Date, ct);
        if (existing is null)
            await _db.ActivityDailySummaries.AddAsync(summary, ct);
        else
        {
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
        }
    }

    public Task<ActivityDailySummary?> GetDailySummaryAsync(Guid employeeId, DateOnly date, CancellationToken ct) =>
        _db.ActivityDailySummaries
            .FirstOrDefaultAsync(s => s.EmployeeId == employeeId && s.Date == date, ct);

    public async Task<IReadOnlyList<ActivitySnapshot>> GetSnapshotsAsync(
        Guid employeeId, DateOnly date, CancellationToken ct) =>
        await GetSnapshotsForDayAsync(employeeId, date, ct);

    public async Task<IReadOnlyList<ApplicationUsage>> GetAppUsageAsync(
        Guid employeeId, DateOnly date, CancellationToken ct) =>
        await GetAppUsageForDayAsync(employeeId, date, ct);

    public async Task<IReadOnlyList<MeetingSession>> GetMeetingsAsync(
        Guid employeeId, DateOnly date, CancellationToken ct) =>
        await GetMeetingsForDayAsync(employeeId, date, ct);

    public async Task<IReadOnlyList<ApplicationCategory>> GetCategoriesAsync(CancellationToken ct) =>
        await _db.ApplicationCategories.ToListAsync(ct);

    public async Task AddCategoryAsync(ApplicationCategory category, CancellationToken ct) =>
        await _db.ApplicationCategories.AddAsync(category, ct);

    public async Task<bool> DeleteCategoryAsync(Guid id, CancellationToken ct)
    {
        var rows = await _db.ApplicationCategories
            .Where(c => c.Id == id)
            .ExecuteDeleteAsync(ct);
        return rows > 0;
    }

    public async Task<int> DeleteRawBufferOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct) =>
        await _db.ActivityRawBuffer
            .Where(b => b.ReceivedAt < cutoff)
            .ExecuteDeleteAsync(ct);

    public async Task<int> DeleteSnapshotsOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct) =>
        await _db.ActivitySnapshots
            .Where(s => s.CapturedAt < cutoff)
            .ExecuteDeleteAsync(ct);
}
