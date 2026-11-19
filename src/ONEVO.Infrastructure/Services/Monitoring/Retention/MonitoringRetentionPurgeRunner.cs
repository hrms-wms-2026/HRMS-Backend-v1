using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Options;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.Monitoring.Retention;

/// <summary>
/// Purges monitoring data that had no retention job (MON-02): activity_raw_buffer rows past
/// their retention window, and screenshot evidence (monitoring_evidence_assets + its backing
/// file) past its own window, skipping anything under legal hold. Both tables are under FORCE
/// row-level security with an admin-or-matching-tenant policy, same as agent_commands - a
/// background scope otherwise defaults to system mode, which that policy admits for neither
/// USING nor WITH CHECK, so every delete here would silently match zero rows. Admin mode is a
/// single cross-tenant operation (no per-tenant loop needed), the same pattern already used by
/// AgentCommandExpiryJob.
/// </summary>
public sealed class MonitoringRetentionPurgeRunner : IMonitoringRetentionPurgeRunner
{
    private readonly ApplicationDbContext _db;
    private readonly IFileStorageService _fileStorage;
    private readonly IWritableTenantContext _tenantContext;
    private readonly IDateTimeProvider _clock;
    private readonly MonitoringRetentionOptions _options;
    private readonly ILogger<MonitoringRetentionPurgeRunner> _logger;

    public MonitoringRetentionPurgeRunner(
        ApplicationDbContext db,
        IFileStorageService fileStorage,
        IWritableTenantContext tenantContext,
        IDateTimeProvider clock,
        IOptions<MonitoringRetentionOptions> options,
        ILogger<MonitoringRetentionPurgeRunner> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _tenantContext = tenantContext;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MonitoringRetentionPurgeResult> RunOnceAsync(CancellationToken ct = default)
    {
        _tenantContext.SetAdminMode();

        var now = _clock.UtcNow;

        var rawBufferCutoff = now.AddDays(-_options.RawBufferRetentionDays);
        var rawBuffersDeleted = await _db.ActivityRawBuffers
            .Where(b => b.ReceivedAt <= rawBufferCutoff)
            .ExecuteDeleteAsync(ct);

        var screenshotCutoff = now.AddDays(-_options.ScreenshotRetentionDays);
        var expiredScreenshots = await _db.MonitoringEvidenceAssets
            .Where(a => a.CapturedAt <= screenshotCutoff && a.LegalHoldId == null)
            .Select(a => new { a.Id, a.TenantId, a.FileRecordId })
            .ToListAsync(ct);

        var screenshotsDeleted = 0;
        foreach (var asset in expiredScreenshots)
        {
            ct.ThrowIfCancellationRequested();

            // Delete the backing object + file_records row first - IFileStorageService.DeleteAsync
            // is idempotent, so a prior partial run that deleted the file but left the evidence
            // row (e.g. the process was killed between the two deletes below) simply succeeds
            // again here with no duplicate work.
            var fileResult = await _fileStorage.DeleteAsync(asset.TenantId, Guid.Empty, asset.FileRecordId, ct);
            if (!fileResult.IsSuccess)
            {
                _logger.LogWarning(
                    "Monitoring retention purge: failed to delete backing file for evidence asset {AssetId} (tenant {TenantId}): {Error}. Evidence row left in place for the next run.",
                    asset.Id, asset.TenantId, fileResult.Error);
                continue;
            }

            screenshotsDeleted += await _db.MonitoringEvidenceAssets
                .Where(a => a.Id == asset.Id)
                .ExecuteDeleteAsync(ct);
        }

        if (rawBuffersDeleted > 0 || screenshotsDeleted > 0)
        {
            _logger.LogInformation(
                "Monitoring retention purge: deleted {RawBuffers} raw buffer row(s) older than {RawDays}d and {Screenshots} screenshot evidence row(s) older than {ScreenshotDays}d.",
                rawBuffersDeleted, _options.RawBufferRetentionDays, screenshotsDeleted, _options.ScreenshotRetentionDays);
        }

        return new MonitoringRetentionPurgeResult(rawBuffersDeleted, screenshotsDeleted);
    }
}
