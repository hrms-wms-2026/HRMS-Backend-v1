using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;

public interface IActivityMonitoringRepository
{
    // Raw buffer
    Task<IReadOnlyList<RawBufferItem>> GetPendingRawBatchAsync(int maxRows, CancellationToken ct);
    Task BulkInsertSnapshotsAsync(IEnumerable<ActivitySnapshot> snapshots, CancellationToken ct);
    Task BulkInsertApplicationUsageAsync(IEnumerable<ApplicationUsage> usage, CancellationToken ct);
    Task BulkInsertMeetingSessionsAsync(IEnumerable<MeetingSession> sessions, CancellationToken ct);
    Task UpsertDeviceTrackingAsync(DeviceTracking tracking, CancellationToken ct);
    Task DeleteRawBufferRowsAsync(IEnumerable<Guid> ids, CancellationToken ct);

    // Daily aggregation
    Task<IReadOnlyList<ActivitySnapshot>> GetSnapshotsForDayAsync(Guid employeeId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<ApplicationUsage>> GetAppUsageForDayAsync(Guid employeeId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<MeetingSession>> GetMeetingsForDayAsync(Guid employeeId, DateOnly date, CancellationToken ct);
    Task UpsertDailySummaryAsync(ActivityDailySummary summary, CancellationToken ct);
    Task AddMonitoringEvidenceAsync(
        MonitoringEvidenceAsset evidence, CancellationToken ct);

    // Queries
    Task<ActivityDailySummary?> GetDailySummaryAsync(Guid employeeId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<ActivitySnapshot>> GetSnapshotsAsync(Guid employeeId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<ApplicationUsage>> GetAppUsageAsync(Guid employeeId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<MeetingSession>> GetMeetingsAsync(Guid employeeId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<ApplicationCategory>> GetCategoriesAsync(CancellationToken ct);
    Task AddCategoryAsync(ApplicationCategory category, CancellationToken ct);
    Task<bool> DeleteCategoryAsync(Guid id, CancellationToken ct);

    // Consent events — returns false when incident_id already has a terminal decision
    Task<bool> AddConsentEventAsync(MonitoringConsentEvent consent, CancellationToken ct);
    Task<IReadOnlyList<MonitoringConsentEvent>> GetConsentNoticesAsync(
        Guid employeeId, DateOnly date, CancellationToken ct);

    // Purge
    Task<int> DeleteRawBufferOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct);
    Task<int> DeleteSnapshotsOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct);
}

// Projection for raw buffer processing
public record RawBufferItem(Guid Id, Guid TenantId, Guid AgentDeviceId, DateTimeOffset ReceivedAt, string PayloadJson);
