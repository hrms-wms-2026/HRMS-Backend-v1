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
/// Polls every active tenant's calendar_event_meetings whose event has ended and whose
/// attendance has never been synced, pulls each one's Teams attendance report, and writes
/// calendar_event_meeting_attendances. Same admin-mode tenant enumeration shape as
/// CalendarSyncJob - see that class's own doc comment for why SetAdminMode() is required first.
/// </summary>
public sealed class TeamsAttendanceSyncJob(IServiceProvider services, ILogger<TeamsAttendanceSyncJob> logger) : BackgroundService
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
                catch (Exception ex) { logger.LogError(ex, "TeamsAttendanceSyncJob run failed."); }
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
                    var teamsClient = scope.ServiceProvider.GetRequiredService<ITeamsMeetingClient>();
                    var attendances = scope.ServiceProvider.GetRequiredService<ICalendarEventMeetingAttendanceRepository>();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                    foreach (var meeting in await meetings.GetDueForAttendanceSyncAsync(tenant.Id, CalendarEventMeetingProviders.MicrosoftTeams, ct))
                    {
                        try
                        {
                            await SyncOneMeetingAsync(tenant.Id, meeting, connections, tokenProvider, teamsClient, attendances, meetings, unitOfWork, ct);
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

    /// <summary>Once the report settled (synced, or unavailable / failed at the cutoff) the meeting's participants are re-evaluated
    /// against the attendance rules (plan C2 post-meeting trigger).</summary>
    private static async Task QueueEvaluationWhenSettledAsync(
        IServiceProvider services, Guid tenantId, CalendarEventMeeting meeting, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        if (meeting.LastAttendanceSyncedAt is null && meeting.AttendanceUnavailableAt is null && meeting.AttendanceSyncFailedAt is null)
            return;
        if (services.GetService<ICalendarNotificationSender>() is not { } sender || services.GetService<ICalendarEventRepository>() is not { } events)
            return;
        if (meeting.CalendarEventId is not Guid eventId)
            return; // a meeting series' link: its occurrences are evaluated by the Phase 2 attendance sync
        var participants = await events.GetParticipantsForEventsAsync(tenantId, [eventId], ct);
        if (participants is null || !participants.TryGetValue(eventId, out var list) || list.Count == 0)
            return;
        await sender.QueueIncidentEvaluationAsync(tenantId, list.Select(p => p.EmployeeId).ToList(), ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task SyncOneMeetingAsync(
        Guid tenantId, CalendarEventMeeting meeting,
        IExternalCalendarConnectionRepository connections, ICalendarConnectionTokenProvider tokenProvider,
        ITeamsMeetingClient teamsClient, ICalendarEventMeetingAttendanceRepository attendances,
        ICalendarEventMeetingRepository meetings, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        // A meeting series' link is synced per occurrence by the Phase 2 attendance sync, never by this one-shot job.
        if (meeting.CalendarEventId is not Guid eventId)
            return;
        if (await meetings.GetEventWindowAsync(tenantId, eventId, ct) is not { } window)
            return; // the event is gone - nothing to ask the provider about

        string? failure = null;
        Exception? providerError = null;
        IReadOnlyList<TeamsAttendanceRecordDto> records = [];
        var connection = await connections.GetByIdForTenantAsync(tenantId, meeting.ExternalCalendarConnectionId, ct);
        var accessToken = connection is null ? null : await tokenProvider.GetFreshAccessTokenAsync(connection, "microsoft", ct);
        if (connection is null)
            failure = CalendarAttendanceSyncErrors.NotConnected;
        else if (accessToken is null)
            failure = CalendarAttendanceSyncErrors.ReauthRequired;
        else
        {
            try
            {
                // The scheduled window, so a link reused by later occurrences still yields this session (E6b/E6c).
                records = await teamsClient.GetAttendanceAsync(accessToken, meeting.ExternalMeetingId, window.Start, window.End, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failure = CalendarAttendanceSyncErrors.ProviderError;
                providerError = ex;
            }
        }

        var now = DateTimeOffset.UtcNow;
        if (failure is not null)
        {
            if (CalendarAttendanceSyncPolicy.DecideAfterFailure(window.End, now) == CalendarAttendanceSyncOutcome.RetryLater)
            {
                if (providerError is not null)
                    throw providerError; // logged by the run loop; the meeting stays due
                return; // no connection or sign-in needed yet - retried next run once fixed
            }
            logger.LogWarning(providerError,
                "Attendance for meeting {MeetingId} (tenant {TenantId}) could not be fetched by the cutoff ({Reason}); recorded as failed.",
                meeting.Id, tenantId, failure);
            meeting.AttendanceSyncFailedAt = now;
            meeting.AttendanceSyncError = failure;
            meetings.UpdateAttendanceSync(meeting);
            await unitOfWork.SaveChangesAsync(ct);
            return;
        }

        switch (CalendarAttendanceSyncPolicy.Decide(records.Count, window.End, now))
        {
            case CalendarAttendanceSyncOutcome.RetryLater:
                return; // no report yet - the meeting stays due and is retried next run
            case CalendarAttendanceSyncOutcome.Unavailable:
                meeting.AttendanceUnavailableAt = now;
                meetings.UpdateAttendanceSync(meeting);
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
        meetings.UpdateAttendanceSync(meeting);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
