using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Services.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class TeamsAttendanceSyncJobTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static Tenant MakeTenant() => new()
    {
        Id = TenantId, Name = "Acme", Slug = "acme", Status = TenantStatus.Active
    };

    private static CalendarEventMeeting MakeMeeting(string? externalMeetingId = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = Guid.NewGuid(),
        ExternalCalendarConnectionId = Guid.NewGuid(), Provider = CalendarEventMeetingProviders.MicrosoftTeams,
        ExternalMeetingId = externalMeetingId ?? "graph-meeting-1", JoinUrl = "https://teams.microsoft.com/l/meetup-join/abc",
        Status = CalendarEventMeetingStatuses.Active
    };

    private static ExternalCalendarConnection MakeConnection(Guid id) => new()
    {
        Id = id, TenantId = TenantId, UserId = Guid.NewGuid(),
        Provider = CalendarExternalSources.OutlookCalendar, ExternalAccountEmail = "me@acme.com",
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
        meetingsRepoMock.Setup(m => m.GetDueForAttendanceSyncAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEventMeeting> { meeting });

        var connectionsRepoMock = new Mock<IExternalCalendarConnectionRepository>();
        connectionsRepoMock.Setup(c => c.GetByIdForTenantAsync(TenantId, connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        var tokenProviderMock = new Mock<ICalendarConnectionTokenProvider>();
        tokenProviderMock.Setup(t => t.GetFreshAccessTokenAsync(connection, "microsoft", It.IsAny<CancellationToken>()))
            .ReturnsAsync("access-token");

        var teamsClientMock = new Mock<ITeamsMeetingClient>();
        teamsClientMock.Setup(t => t.GetAttendanceAsync("access-token", "graph-meeting-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TeamsAttendanceRecordDto>
            {
                new("Ada Lovelace", "ada@acme.com", DateTimeOffset.UtcNow.AddMinutes(-30), DateTimeOffset.UtcNow),
                new("Grace Hopper", "grace@acme.com", DateTimeOffset.UtcNow.AddMinutes(-20), DateTimeOffset.UtcNow)
            });

        var attendancesRepoMock = new Mock<ICalendarEventMeetingAttendanceRepository>();

        services.AddSingleton(tenantRepoMock.Object);
        services.AddSingleton(meetingsRepoMock.Object);
        services.AddSingleton(connectionsRepoMock.Object);
        services.AddSingleton(tokenProviderMock.Object);
        services.AddSingleton(teamsClientMock.Object);
        services.AddSingleton(attendancesRepoMock.Object);
        services.AddSingleton(Mock.Of<IUnitOfWork>());
        services.AddSingleton(Mock.Of<IWritableTenantContext>());
        services.AddSingleton(Mock.Of<ITenantContextSwitcher>());
        var provider = services.BuildServiceProvider();

        var job = new TeamsAttendanceSyncJob(provider, NullLogger<TeamsAttendanceSyncJob>.Instance);
        await job.RunOnceAsync(CancellationToken.None);

        attendancesRepoMock.Verify(a => a.AddRangeAsync(
            It.Is<IEnumerable<CalendarEventMeetingAttendance>>(records => records.Count() == 2),
            It.IsAny<CancellationToken>()), Times.Once);
        meetingsRepoMock.Verify(m => m.Update(
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
        meetingsRepoMock.Setup(m => m.GetDueForAttendanceSyncAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEventMeeting> { failingMeeting, succeedingMeeting });

        var connectionsRepoMock = new Mock<IExternalCalendarConnectionRepository>();
        connectionsRepoMock.Setup(c => c.GetByIdForTenantAsync(TenantId, failingConnection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(failingConnection);
        connectionsRepoMock.Setup(c => c.GetByIdForTenantAsync(TenantId, succeedingConnection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(succeedingConnection);

        var tokenProviderMock = new Mock<ICalendarConnectionTokenProvider>();
        tokenProviderMock.Setup(t => t.GetFreshAccessTokenAsync(It.IsAny<ExternalCalendarConnection>(), "microsoft", It.IsAny<CancellationToken>()))
            .ReturnsAsync("access-token");

        var teamsClientMock = new Mock<ITeamsMeetingClient>();
        teamsClientMock.Setup(t => t.GetAttendanceAsync("access-token", failingMeeting.ExternalMeetingId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("simulated Graph 500"));
        teamsClientMock.Setup(t => t.GetAttendanceAsync("access-token", succeedingMeeting.ExternalMeetingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TeamsAttendanceRecordDto> { new("Ada Lovelace", "ada@acme.com", DateTimeOffset.UtcNow, null) });

        var attendancesRepoMock = new Mock<ICalendarEventMeetingAttendanceRepository>();

        services.AddSingleton(tenantRepoMock.Object);
        services.AddSingleton(meetingsRepoMock.Object);
        services.AddSingleton(connectionsRepoMock.Object);
        services.AddSingleton(tokenProviderMock.Object);
        services.AddSingleton(teamsClientMock.Object);
        services.AddSingleton(attendancesRepoMock.Object);
        services.AddSingleton(Mock.Of<IUnitOfWork>());
        services.AddSingleton(Mock.Of<IWritableTenantContext>());
        services.AddSingleton(Mock.Of<ITenantContextSwitcher>());
        var provider = services.BuildServiceProvider();

        var job = new TeamsAttendanceSyncJob(provider, NullLogger<TeamsAttendanceSyncJob>.Instance);

        // Should not throw - the failing meeting's exception must be caught and logged, not
        // propagated, and the second meeting must still be synced.
        await job.RunOnceAsync(CancellationToken.None);

        meetingsRepoMock.Verify(m => m.Update(
            It.Is<CalendarEventMeeting>(cm => cm.Id == succeedingMeeting.Id && cm.LastAttendanceSyncedAt != null)), Times.Once);
        meetingsRepoMock.Verify(m => m.Update(
            It.Is<CalendarEventMeeting>(cm => cm.Id == failingMeeting.Id)), Times.Never);
    }
}
