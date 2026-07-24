using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Services.ActivityMonitoring;

public sealed class ProcessRawBufferJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private const int BatchSize = 200;

    private static readonly HashSet<string> MeetingProcesses =
        new(StringComparer.OrdinalIgnoreCase) { "teams.exe", "zoom.exe", "webex.exe", "skype.exe" };

    private readonly IServiceProvider _services;
    private readonly ILogger<ProcessRawBufferJob> _logger;

    public ProcessRawBufferJob(IServiceProvider services, ILogger<ProcessRawBufferJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                await RunBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ProcessRawBufferJob batch failed; will retry next interval.");
            }
        }
    }

    private async Task RunBatchAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var activityRepo = scope.ServiceProvider.GetRequiredService<IActivityMonitoringRepository>();
        var agentRepo = scope.ServiceProvider.GetRequiredService<IAgentGatewayRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var batch = await activityRepo.GetPendingRawBatchAsync(BatchSize, ct);
        if (batch.Count == 0) return;

        var snapshots = new List<ActivitySnapshot>();
        var appUsage = new List<ApplicationUsage>();
        var meetings = new List<MeetingSession>();
        var deviceSessions = new List<(Guid TenantId, Guid EmployeeId, int ActiveMinutes)>();
        var processedIds = new List<Guid>();

        foreach (var item in batch)
        {
            try
            {
                var agent = await agentRepo.GetAgentByIdAsync(item.AgentDeviceId, ct);
                if (agent is null || agent.EmployeeId is null)
                {
                    processedIds.Add(item.Id);
                    continue;
                }

                var employeeId = agent.EmployeeId.Value;
                var tenantId = item.TenantId;

                using var doc = JsonDocument.Parse(item.PayloadJson);
                var root = doc.RootElement;

                if (!root.TryGetProperty("batch", out var batchArray))
                {
                    processedIds.Add(item.Id);
                    continue;
                }

                foreach (var entry in batchArray.EnumerateArray())
                {
                    if (!entry.TryGetProperty("type", out var typeEl)) continue;
                    var type = typeEl.GetString();
                    if (!entry.TryGetProperty("data", out var data)) continue;

                    switch (type)
                    {
                        case "activity_snapshot":
                            snapshots.Add(ParseSnapshot(data, tenantId, employeeId, item.ReceivedAt));
                            break;
                        case "app_usage":
                            appUsage.Add(ParseAppUsage(data, tenantId, employeeId, DateOnly.FromDateTime(item.ReceivedAt.UtcDateTime)));
                            break;
                        case "device_session":
                            if (data.TryGetProperty("active_minutes", out var am))
                                deviceSessions.Add((tenantId, employeeId, am.GetInt32()));
                            break;
                    }
                }

                processedIds.Add(item.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process raw buffer item {Id}; skipping.", item.Id);
                processedIds.Add(item.Id);
            }
        }

        foreach (var usage in appUsage.Where(u => MeetingProcesses.Contains(u.ProcessName)))
        {
            meetings.Add(new MeetingSession
            {
                Id = Guid.NewGuid(),
                TenantId = usage.TenantId,
                EmployeeId = usage.EmployeeId,
                MeetingStart = DateTimeOffset.UtcNow.Date.ToUniversalTime(),
                MeetingEnd = DateTimeOffset.UtcNow,
                Platform = usage.ProcessName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase),
                DurationMinutes = usage.TotalSeconds / 60,
                HadCameraOn = false,
                HadMicActivity = false
            });
        }

        if (snapshots.Count > 0) await activityRepo.BulkInsertSnapshotsAsync(snapshots, ct);
        if (appUsage.Count > 0) await activityRepo.BulkInsertApplicationUsageAsync(appUsage, ct);
        if (meetings.Count > 0) await activityRepo.BulkInsertMeetingSessionsAsync(meetings, ct);

        foreach (var (tenantId, employeeId, activeMinutes) in deviceSessions)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            await activityRepo.UpsertDeviceTrackingAsync(new DeviceTracking
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employeeId,
                Date = today,
                LaptopActiveMinutes = activeMinutes,
                LaptopPercentage = 100,
                DetectionMethod = "agent"
            }, ct);
        }

        await activityRepo.DeleteRawBufferRowsAsync(processedIds, ct);
        await uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "ProcessRawBufferJob: {Snapshots} snapshots, {AppUsage} app records, {Meetings} meetings from {Batch} raw items.",
            snapshots.Count, appUsage.Count, meetings.Count, batch.Count);
    }

    private static ActivitySnapshot ParseSnapshot(JsonElement data, Guid tenantId, Guid employeeId, DateTimeOffset capturedAt)
    {
        var keyboardCount = data.TryGetProperty("keyboard_events_count", out var k) ? k.GetInt32() : 0;
        var mouseCount = data.TryGetProperty("mouse_events_count", out var m) ? m.GetInt32() : 0;
        var activeSeconds = data.TryGetProperty("active_seconds", out var a) ? a.GetInt32() : 0;
        var idleSeconds = data.TryGetProperty("idle_seconds", out var i) ? i.GetInt32() : 0;
        var processName = data.TryGetProperty("foreground_process_name", out var p) ? p.GetString() ?? string.Empty : string.Empty;

        const int maxExpected = 3000;
        var intensity = Math.Min((decimal)(keyboardCount + mouseCount) / maxExpected * 100, 100);

        return new ActivitySnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            CapturedAt = capturedAt,
            KeyboardEventsCount = keyboardCount,
            MouseEventsCount = mouseCount,
            ActiveSeconds = activeSeconds,
            IdleSeconds = idleSeconds,
            IntensityScore = intensity,
            ForegroundProcessName = processName,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static ApplicationUsage ParseAppUsage(JsonElement data, Guid tenantId, Guid employeeId, DateOnly date)
    {
        var processName = data.TryGetProperty("process_name", out var p) ? p.GetString() ?? string.Empty : string.Empty;
        var appName = data.TryGetProperty("application_name", out var a) ? a.GetString() ?? string.Empty : string.Empty;
        var category = data.TryGetProperty("app_category_type", out var c) ? c.GetString() : null;
        var titleHash = data.TryGetProperty("window_title_hash", out var h) ? h.GetString() : null;
        var duration = data.TryGetProperty("duration_seconds", out var d) ? d.GetInt32() : 0;

        return new ApplicationUsage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            Date = date,
            ProcessName = processName,
            ApplicationName = appName,
            ApplicationCategory = category,
            WindowTitleHash = titleHash,
            TotalSeconds = duration
        };
    }
}
