using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
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

    private static async Task SyncOneMeetingAsync(
        Guid tenantId, CalendarEventMeeting meeting,
        IExternalCalendarConnectionRepository connections, ICalendarConnectionTokenProvider tokenProvider,
        ITeamsMeetingClient teamsClient, ICalendarEventMeetingAttendanceRepository attendances,
        ICalendarEventMeetingRepository meetings, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var connection = await connections.GetByIdForTenantAsync(tenantId, meeting.ExternalCalendarConnectionId, ct);
        if (connection is null)
            return;

        var accessToken = await tokenProvider.GetFreshAccessTokenAsync(connection, "microsoft", ct);
        if (accessToken is null)
            return; // reauth required - retried automatically next run once the connection is fixed

        var records = await teamsClient.GetAttendanceAsync(accessToken, meeting.ExternalMeetingId, ct);
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
