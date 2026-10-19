using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Services.Calendar;

/// <summary>
/// Polls every active tenant's calendar_event_meetings (Provider = "zoom") whose event has ended
/// and whose attendance has never been synced, pulls each one's Zoom past-meeting participant
/// report, and writes calendar_event_meeting_attendances. Mirrors TeamsAttendanceSyncJob exactly
/// (same admin-mode tenant enumeration shape as CalendarSyncJob) - see that class's doc comments
/// for why SetAdminMode() is required first.
/// </summary>
public sealed class ZoomAttendanceSyncJob(IServiceProvider services, ILogger<ZoomAttendanceSyncJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private const int TenantPageSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await RunOnceAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "ZoomAttendanceSyncJob run failed."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // PeriodicTimer.WaitForNextTickAsync throws when the token passed to it is
            // cancelled (unlike disposing the timer itself, which returns false instead) -
            // an ordinary host shutdown must not surface as an unhandled exception here,
            // since HostOptions.BackgroundServiceExceptionBehavior = StopHost treats any
            // unhandled exception from a BackgroundService as a crash.
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();
        tenantContext.SetAdminMode();

        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var switcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();

        var skip = 0;
        while (true)
        {
            var page = await tenants.ListAsync(TenantStatus.Active, searchTerm: null, skip, TenantPageSize, ct);
            if (page.Count == 0) break;

            foreach (var tenant in page)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

                    var meetings = scope.ServiceProvider.GetRequiredService<ICalendarEventMeetingRepository>();
                    var connections = scope.ServiceProvider.GetRequiredService<IExternalCalendarConnectionRepository>();
                    var tokenProvider = scope.ServiceProvider.GetRequiredService<ICalendarConnectionTokenProvider>();
                    var zoomClient = scope.ServiceProvider.GetRequiredService<IZoomMeetingClient>();
                    var attendances = scope.ServiceProvider.GetRequiredService<ICalendarEventMeetingAttendanceRepository>();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                    foreach (var meeting in await meetings.GetDueForAttendanceSyncAsync(tenant.Id, CalendarEventMeetingProviders.Zoom, ct))
                    {
                        try
                        {
                            await SyncOneMeetingAsync(tenant.Id, meeting, connections, tokenProvider, zoomClient, attendances, meetings, unitOfWork, ct);
                            await QueueEvaluationWhenSettledAsync(scope.ServiceProvider, tenant.Id, meeting, unitOfWork, ct);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Attendance sync failed for meeting {MeetingId} (tenant {TenantId}); skipping.", meeting.Id, tenant.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Attendance sync failed for tenant {TenantId}; skipping.", tenant.Id);
                }
            }

            skip += TenantPageSize;
        }
    }

    /// <summary>Once the report settled (synced, or unavailable at the cutoff) the meeting's participants are re-evaluated
    /// against the attendance rules (plan C2 post-meeting trigger).</summary>
    private static async Task QueueEvaluationWhenSettledAsync(
        IServiceProvider services, Guid tenantId, CalendarEventMeeting meeting, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        if (meeting.LastAttendanceSyncedAt is null && meeting.AttendanceUnavailableAt is null)
            return;
        if (services.GetService<ICalendarNotificationSender>() is not { } sender || services.GetService<ICalendarEventRepository>() is not { } events)
            return;
        var participants = await events.GetParticipantsForEventsAsync(tenantId, [meeting.CalendarEventId], ct);
        if (participants is null || !participants.TryGetValue(meeting.CalendarEventId, out var list) || list.Count == 0)
            return;
        await sender.QueueIncidentEvaluationAsync(tenantId, list.Select(p => p.EmployeeId).ToList(), ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static async Task SyncOneMeetingAsync(
        Guid tenantId, CalendarEventMeeting meeting,
        IExternalCalendarConnectionRepository connections, ICalendarConnectionTokenProvider tokenProvider,
        IZoomMeetingClient zoomClient, ICalendarEventMeetingAttendanceRepository attendances,
        ICalendarEventMeetingRepository meetings, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var connection = await connections.GetByIdForTenantAsync(tenantId, meeting.ExternalCalendarConnectionId, ct);
        if (connection is null)
            return;

        var accessToken = await tokenProvider.GetFreshAccessTokenAsync(connection, "zoom", ct);
        if (accessToken is null)
            return; // reauth required - retried automatically next run once the connection is fixed

        var records = await zoomClient.GetAttendanceAsync(accessToken, meeting.ExternalMeetingId, ct);
        var now = DateTimeOffset.UtcNow;
        switch (CalendarAttendanceSyncPolicy.Decide(records.Count, await meetings.GetEventEndAsync(tenantId, meeting.CalendarEventId, ct), now))
        {
            case CalendarAttendanceSyncOutcome.RetryLater:
                return; // no report yet - the meeting stays due and is retried next run
            case CalendarAttendanceSyncOutcome.Unavailable:
                meeting.AttendanceUnavailableAt = now;
                meetings.Update(meeting);
                await unitOfWork.SaveChangesAsync(ct);
                return;
        }

        var toAdd = records.Select(r => new CalendarEventMeetingAttendance
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventMeetingId = meeting.Id,
            ExternalParticipantName = r.ParticipantName, ExternalParticipantEmail = r.ParticipantEmail,
            JoinedAt = r.JoinedAt, LeftAt = r.LeftAt,
            DurationSeconds = r.LeftAt.HasValue ? (int)(r.LeftAt.Value - r.JoinedAt).TotalSeconds : null,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await attendances.AddRangeAsync(toAdd, ct);

        meeting.LastAttendanceSyncedAt = DateTimeOffset.UtcNow;
        meetings.Update(meeting);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
