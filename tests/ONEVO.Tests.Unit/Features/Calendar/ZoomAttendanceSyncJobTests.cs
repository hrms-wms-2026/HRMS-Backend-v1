using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Services.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class ZoomAttendanceSyncJobTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static Tenant MakeTenant() => new()
    {
        Id = TenantId, Name = "Acme", Slug = "acme", Status = TenantStatus.Active
    };

    private static CalendarEventMeeting MakeMeeting(string? externalMeetingId = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = Guid.NewGuid(),
        ExternalCalendarConnectionId = Guid.NewGuid(), Provider = CalendarEventMeetingProviders.Zoom,
        ExternalMeetingId = externalMeetingId ?? "graph-meeting-1", JoinUrl = "https://teams.microsoft.com/l/meetup-join/abc",
        Status = CalendarEventMeetingStatuses.Active
    };

    private static ExternalCalendarConnection MakeConnection(Guid id) => new()
    {
        Id = id, TenantId = TenantId, UserId = Guid.NewGuid(),
        Provider = CalendarExternalSources.Zoom, ExternalAccountEmail = "me@acme.com",
        RefreshTokenEncrypted = [1], Status = ExternalCalendarConnectionStatuses.Active
    };

    [Fact]
    public async Task RunOnceAsync_MeetingDueForSync_WritesAttendanceRecordsAndStampsSyncedAt()
    {
        var services = new ServiceCollection();

        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock.Setup(t => t.ListAsync(TenantStatus.Active, null, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant> { MakeTenant() });
        tenantRepoMock.Setup(t => t.ListAsync(TenantStatus.Active, null, 100, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant>());

        var meeting = MakeMeeting();
        var connection = MakeConnection(meeting.ExternalCalendarConnectionId);

        var meetingsRepoMock = new Mock<ICalendarEventMeetingRepository>();
        meetingsRepoMock.Setup(m => m.GetEventWindowAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventWindow(DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1)));
        meetingsRepoMock.Setup(m => m.GetDueForAttendanceSyncAsync(TenantId, CalendarEventMeetingProviders.Zoom, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEventMeeting> { meeting });

        var connectionsRepoMock = new Mock<IExternalCalendarConnectionRepository>();
        connectionsRepoMock.Setup(c => c.GetByIdForTenantAsync(TenantId, connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        var tokenProviderMock = new Mock<ICalendarConnectionTokenProvider>();
        tokenProviderMock.Setup(t => t.GetFreshAccessTokenAsync(connection, "zoom", It.IsAny<CancellationToken>()))
            .ReturnsAsync("access-token");

        var zoomClientMock = new Mock<IZoomMeetingClient>();
        zoomClientMock.Setup(t => t.GetAttendanceAsync("access-token", "graph-meeting-1", It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ZoomAttendanceRecordDto>
            {
                new("Ada Lovelace", "ada@acme.com", DateTimeOffset.UtcNow.AddMinutes(-30), DateTimeOffset.UtcNow),
                new("Grace Hopper", "grace@acme.com", DateTimeOffset.UtcNow.AddMinutes(-20), DateTimeOffset.UtcNow)
            });

        var attendancesRepoMock = new Mock<ICalendarEventMeetingAttendanceRepository>();

        services.AddSingleton(tenantRepoMock.Object);
        services.AddSingleton(meetingsRepoMock.Object);
        services.AddSingleton(connectionsRepoMock.Object);
        services.AddSingleton(tokenProviderMock.Object);
        services.AddSingleton(zoomClientMock.Object);
        services.AddSingleton(attendancesRepoMock.Object);
        services.AddSingleton(Mock.Of<IUnitOfWork>());
        services.AddSingleton(Mock.Of<IWritableTenantContext>());
        services.AddSingleton(Mock.Of<ITenantContextSwitcher>());
        var provider = services.BuildServiceProvider();

        var job = new ZoomAttendanceSyncJob(provider, NullLogger<ZoomAttendanceSyncJob>.Instance);
        await job.RunOnceAsync(CancellationToken.None);

        attendancesRepoMock.Verify(a => a.AddRangeAsync(
            It.Is<IEnumerable<CalendarEventMeetingAttendance>>(records => records.Count() == 2),
            It.IsAny<CancellationToken>()), Times.Once);
        meetingsRepoMock.Verify(m => m.UpdateAttendanceSync(
            It.Is<CalendarEventMeeting>(cm => cm.Id == meeting.Id && cm.LastAttendanceSyncedAt != null)), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_OneMeetingThrows_OtherMeetingsInSameTenantStillSync()
    {
        var services = new ServiceCollection();

        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock.Setup(t => t.ListAsync(TenantStatus.Active, null, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant> { MakeTenant() });
        tenantRepoMock.Setup(t => t.ListAsync(TenantStatus.Active, null, 100, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant>());

        var failingMeeting = MakeMeeting("graph-meeting-failing");
        var succeedingMeeting = MakeMeeting("graph-meeting-succeeding");
        var failingConnection = MakeConnection(failingMeeting.ExternalCalendarConnectionId);
        var succeedingConnection = MakeConnection(succeedingMeeting.ExternalCalendarConnectionId);

        var meetingsRepoMock = new Mock<ICalendarEventMeetingRepository>();
        meetingsRepoMock.Setup(m => m.GetEventWindowAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventWindow(DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1)));
        meetingsRepoMock.Setup(m => m.GetDueForAttendanceSyncAsync(TenantId, CalendarEventMeetingProviders.Zoom, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEventMeeting> { failingMeeting, succeedingMeeting });

        var connectionsRepoMock = new Mock<IExternalCalendarConnectionRepository>();
        connectionsRepoMock.Setup(c => c.GetByIdForTenantAsync(TenantId, failingConnection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(failingConnection);
        connectionsRepoMock.Setup(c => c.GetByIdForTenantAsync(TenantId, succeedingConnection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(succeedingConnection);

        var tokenProviderMock = new Mock<ICalendarConnectionTokenProvider>();
        tokenProviderMock.Setup(t => t.GetFreshAccessTokenAsync(It.IsAny<ExternalCalendarConnection>(), "zoom", It.IsAny<CancellationToken>()))
            .ReturnsAsync("access-token");

        var zoomClientMock = new Mock<IZoomMeetingClient>();
        zoomClientMock.Setup(t => t.GetAttendanceAsync("access-token", failingMeeting.ExternalMeetingId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("simulated Zoom 500"));
        zoomClientMock.Setup(t => t.GetAttendanceAsync("access-token", succeedingMeeting.ExternalMeetingId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ZoomAttendanceRecordDto> { new("Ada Lovelace", "ada@acme.com", DateTimeOffset.UtcNow, null) });

        var attendancesRepoMock = new Mock<ICalendarEventMeetingAttendanceRepository>();

        services.AddSingleton(tenantRepoMock.Object);
        services.AddSingleton(meetingsRepoMock.Object);
        services.AddSingleton(connectionsRepoMock.Object);
        services.AddSingleton(tokenProviderMock.Object);
        services.AddSingleton(zoomClientMock.Object);
        services.AddSingleton(attendancesRepoMock.Object);
        services.AddSingleton(Mock.Of<IUnitOfWork>());
        services.AddSingleton(Mock.Of<IWritableTenantContext>());
        services.AddSingleton(Mock.Of<ITenantContextSwitcher>());
        var provider = services.BuildServiceProvider();

        var job = new ZoomAttendanceSyncJob(provider, NullLogger<ZoomAttendanceSyncJob>.Instance);

        // Should not throw - the failing meeting's exception must be caught and logged, not
        // propagated, and the second meeting must still be synced.
        await job.RunOnceAsync(CancellationToken.None);

        meetingsRepoMock.Verify(m => m.UpdateAttendanceSync(
            It.Is<CalendarEventMeeting>(cm => cm.Id == succeedingMeeting.Id && cm.LastAttendanceSyncedAt != null)), Times.Once);
        meetingsRepoMock.Verify(m => m.UpdateAttendanceSync(
            It.Is<CalendarEventMeeting>(cm => cm.Id == failingMeeting.Id)), Times.Never);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(25, true)]
    public async Task RunOnceAsync_EmptyReport_IsRetriedUntilTheCutoffThenMarkedUnavailable(int hoursSinceEnd, bool unavailable)
    {
        var services = new ServiceCollection();
        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock.Setup(t => t.ListAsync(TenantStatus.Active, null, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant> { MakeTenant() });
        tenantRepoMock.Setup(t => t.ListAsync(TenantStatus.Active, null, 100, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant>());
        var meeting = MakeMeeting();
        var connection = MakeConnection(meeting.ExternalCalendarConnectionId);
        var meetingsRepoMock = new Mock<ICalendarEventMeetingRepository>();
        meetingsRepoMock.Setup(m => m.GetEventWindowAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventWindow(DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1)));
        meetingsRepoMock.Setup(m => m.GetDueForAttendanceSyncAsync(TenantId, CalendarEventMeetingProviders.Zoom, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEventMeeting> { meeting });
        meetingsRepoMock.Setup(m => m.GetEventWindowAsync(TenantId, meeting.CalendarEventId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventWindow(DateTimeOffset.UtcNow.AddHours(-hoursSinceEnd).AddMinutes(-30), DateTimeOffset.UtcNow.AddHours(-hoursSinceEnd)));
        var connectionsRepoMock = new Mock<IExternalCalendarConnectionRepository>();
        connectionsRepoMock.Setup(c => c.GetByIdForTenantAsync(TenantId, connection.Id, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        var tokenProviderMock = new Mock<ICalendarConnectionTokenProvider>();
        tokenProviderMock.Setup(t => t.GetFreshAccessTokenAsync(connection, "zoom", It.IsAny<CancellationToken>())).ReturnsAsync("access-token");
        var clientMock = new Mock<IZoomMeetingClient>();
        clientMock.Setup(t => t.GetAttendanceAsync("access-token", "graph-meeting-1", It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ZoomAttendanceRecordDto>());
        var attendancesRepoMock = new Mock<ICalendarEventMeetingAttendanceRepository>();
        services.AddSingleton(tenantRepoMock.Object);
        services.AddSingleton(meetingsRepoMock.Object);
        services.AddSingleton(connectionsRepoMock.Object);
        services.AddSingleton(tokenProviderMock.Object);
        services.AddSingleton(clientMock.Object);
        services.AddSingleton(attendancesRepoMock.Object);
        services.AddSingleton(Mock.Of<IUnitOfWork>());
        services.AddSingleton(Mock.Of<IWritableTenantContext>());
        services.AddSingleton(Mock.Of<ITenantContextSwitcher>());

        await new ZoomAttendanceSyncJob(services.BuildServiceProvider(), NullLogger<ZoomAttendanceSyncJob>.Instance).RunOnceAsync(CancellationToken.None);

        // Never stamped as synced on an empty report: within the cutoff it stays due; after it, it is marked unavailable.
        meetingsRepoMock.Verify(m => m.UpdateAttendanceSync(It.Is<CalendarEventMeeting>(cm => cm.LastAttendanceSyncedAt != null)), Times.Never);
        meetingsRepoMock.Verify(m => m.UpdateAttendanceSync(It.Is<CalendarEventMeeting>(cm => cm.AttendanceUnavailableAt != null)), unavailable ? Times.Once() : Times.Never());
    }

    private static (Mock<ICalendarEventMeetingRepository> Meetings, Mock<IZoomMeetingClient> Client, ZoomAttendanceSyncJob Job, CalendarEventMeeting Meeting)
        BuildJob(int hoursSinceEnd, bool connectionExists = true, string? accessToken = "access-token", Exception? providerError = null)
    {
        var services = new ServiceCollection();
        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock.Setup(t => t.ListAsync(TenantStatus.Active, null, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant> { MakeTenant() });
        tenantRepoMock.Setup(t => t.ListAsync(TenantStatus.Active, null, 100, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant>());
        var meeting = MakeMeeting();
        var connection = MakeConnection(meeting.ExternalCalendarConnectionId);
        var meetingsRepoMock = new Mock<ICalendarEventMeetingRepository>();
        meetingsRepoMock.Setup(m => m.GetDueForAttendanceSyncAsync(TenantId, CalendarEventMeetingProviders.Zoom, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEventMeeting> { meeting });
        meetingsRepoMock.Setup(m => m.GetEventWindowAsync(TenantId, meeting.CalendarEventId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventWindow(EventEnd(hoursSinceEnd).AddMinutes(-30), EventEnd(hoursSinceEnd)));
        var connectionsRepoMock = new Mock<IExternalCalendarConnectionRepository>();
        connectionsRepoMock.Setup(c => c.GetByIdForTenantAsync(TenantId, connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connectionExists ? connection : null);
        var tokenProviderMock = new Mock<ICalendarConnectionTokenProvider>();
        tokenProviderMock.Setup(t => t.GetFreshAccessTokenAsync(connection, "zoom", It.IsAny<CancellationToken>())).ReturnsAsync(accessToken);
        var clientMock = new Mock<IZoomMeetingClient>();
        var setup = clientMock.Setup(t => t.GetAttendanceAsync("access-token", meeting.ExternalMeetingId,
            It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()));
        if (providerError is not null)
            setup.ThrowsAsync(providerError);
        else
            setup.ReturnsAsync(new List<ZoomAttendanceRecordDto> { new("Ada Lovelace", "ada@acme.com", DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1)) });
        services.AddSingleton(tenantRepoMock.Object);
        services.AddSingleton(meetingsRepoMock.Object);
        services.AddSingleton(connectionsRepoMock.Object);
        services.AddSingleton(tokenProviderMock.Object);
        services.AddSingleton(clientMock.Object);
        services.AddSingleton(Mock.Of<ICalendarEventMeetingAttendanceRepository>());
        services.AddSingleton(Mock.Of<IUnitOfWork>());
        services.AddSingleton(Mock.Of<IWritableTenantContext>());
        services.AddSingleton(Mock.Of<ITenantContextSwitcher>());
        return (meetingsRepoMock, clientMock, new ZoomAttendanceSyncJob(services.BuildServiceProvider(), NullLogger<ZoomAttendanceSyncJob>.Instance), meeting);
    }

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static DateTimeOffset EventEnd(int hoursSinceEnd) => Now.AddHours(-hoursSinceEnd);

    [Fact]
    public async Task RunOnceAsync_AsksTheProviderForTheSessionOfTheScheduledEvent()
    {
        var (_, client, job, meeting) = BuildJob(hoursSinceEnd: 2);

        await job.RunOnceAsync(CancellationToken.None);

        client.Verify(c => c.GetAttendanceAsync("access-token", meeting.ExternalMeetingId,
            EventEnd(2).AddMinutes(-30), EventEnd(2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("provider_error")]
    [InlineData("not_connected")]
    [InlineData("reauth_required")]
    public async Task RunOnceAsync_AFailurePastTheCutoff_IsRecordedAsSyncFailedAndStopsRetrying(string reason)
    {
        var (meetings, _, job, meeting) = reason switch
        {
            "not_connected" => BuildJob(hoursSinceEnd: 25, connectionExists: false),
            "reauth_required" => BuildJob(hoursSinceEnd: 25, accessToken: null),
            _ => BuildJob(hoursSinceEnd: 25, providerError: new HttpRequestException("simulated 500")),
        };

        await job.RunOnceAsync(CancellationToken.None);

        meetings.Verify(m => m.UpdateAttendanceSync(It.Is<CalendarEventMeeting>(cm =>
            cm.Id == meeting.Id && cm.AttendanceSyncFailedAt != null && cm.AttendanceSyncError == reason
            && cm.AttendanceUnavailableAt == null && cm.LastAttendanceSyncedAt == null)), Times.Once);
    }

    [Theory]
    [InlineData("provider_error")]
    [InlineData("not_connected")]
    [InlineData("reauth_required")]
    public async Task RunOnceAsync_AFailureBeforeTheCutoff_IsRetriedNextRun(string reason)
    {
        var (meetings, _, job, _) = reason switch
        {
            "not_connected" => BuildJob(hoursSinceEnd: 2, connectionExists: false),
            "reauth_required" => BuildJob(hoursSinceEnd: 2, accessToken: null),
            _ => BuildJob(hoursSinceEnd: 2, providerError: new HttpRequestException("simulated 500")),
        };

        await job.RunOnceAsync(CancellationToken.None);

        meetings.Verify(m => m.UpdateAttendanceSync(It.IsAny<CalendarEventMeeting>()), Times.Never);
    }
}
