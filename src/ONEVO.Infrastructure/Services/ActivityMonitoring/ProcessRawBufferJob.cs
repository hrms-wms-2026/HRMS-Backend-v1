using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.Commands.Screenshot;
using ONEVO.Application.Features.AgentGateway.DTOs;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Services.ActivityMonitoring;

public sealed class ProcessRawBufferJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private const int BatchSize = 200;

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
        var screenshotScheduler =
            scope.ServiceProvider.GetRequiredService<IScreenshotCommandScheduler>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var batch = await activityRepo.GetPendingRawBatchAsync(BatchSize, ct);
        if (batch.Count == 0) return;

        var snapshots = new List<ActivitySnapshot>();
        var appUsage = new List<ApplicationUsage>();
        var meetings = new List<MeetingSession>();
        var deviceSessions = new List<(Guid TenantId, Guid EmployeeId, int ActiveMinutes)>();
        var processedIds = new List<Guid>();
        var screenshotScheduledAgents = new HashSet<Guid>();

        foreach (var item in batch)
        {
            try
            {
                var agent = await agentRepo.GetAgentByIdAsync(item.AgentDeviceId, ct);
                if (agent is null ||
                    agent.EmployeeId is null ||
                    agent.TenantId != item.TenantId ||
                    !string.Equals(agent.Status, "active", StringComparison.Ordinal))
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

                    var envelope = DeserializeEnvelope(entry);
                    if (envelope is null) continue;

                    switch (type)
                    {
                        case "activity_snapshot":
                            var snapshot = ActivityEventParser.ParseActivitySnapshot(envelope, tenantId, employeeId);
                            snapshots.Add(snapshot);
                            if (!screenshotScheduledAgents.Contains(agent.Id) &&
                                await screenshotScheduler.TryScheduleAsync(
                                    agent,
                                    snapshot,
                                    ct))
                            {
                                screenshotScheduledAgents.Add(agent.Id);
                            }
                            break;
                        case "app_usage":
                            var usage = ActivityEventParser.ParseAppUsage(envelope, tenantId, employeeId);
                            if (usage is not null) appUsage.Add(usage);
                            break;
                        case "meeting_app_usage":
                            var meeting = ActivityEventParser.ParseMeetingAppUsage(envelope, tenantId, employeeId);
                            if (meeting is not null) meetings.Add(meeting);
                            break;
                        case "device_session":
                            if (envelope.Data.TryGetProperty("active_minutes", out var am))
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

    private static AgentEventEnvelope? DeserializeEnvelope(
        JsonElement entry)
    {
        try
        {
            if (!entry.TryGetProperty("event_id", out var eidEl) ||
                !Guid.TryParse(eidEl.GetString(), out var eventId))
                return null;

            if (!entry.TryGetProperty("captured_at", out var catEl) ||
                !DateTimeOffset.TryParse(catEl.GetString(), out var capturedAt))
                return null;

            if (!entry.TryGetProperty("presence_session_id", out var psEl) ||
                !Guid.TryParse(psEl.GetString(), out var presenceSessionId))
                return null;

            if (!entry.TryGetProperty("data", out var data))
                return null;

            Guid? taskId = null;
            if (entry.TryGetProperty("task_id", out var tidEl) &&
                tidEl.ValueKind == JsonValueKind.String &&
                Guid.TryParse(tidEl.GetString(), out var parsedTaskId))
                taskId = parsedTaskId;

            return new AgentEventEnvelope
            {
                EventId = eventId,
                Type = entry.TryGetProperty("type", out var t) ? t.GetString() ?? string.Empty : string.Empty,
                SchemaVersion = entry.TryGetProperty("schema_version", out var sv) ? sv.GetString() ?? string.Empty : string.Empty,
                CapturedAt = capturedAt,
                PresenceSessionId = presenceSessionId,
                TaskId = taskId,
                Data = data
            };
        }
        catch
        {
            return null;
        }
    }
}
