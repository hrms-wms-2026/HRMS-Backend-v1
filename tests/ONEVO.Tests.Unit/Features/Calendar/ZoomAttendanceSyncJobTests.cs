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

public sealed class ZoomAttendanceSyncJobTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private sealed class Rig
    {
        public Mock<ICalendarEventMeetingRepository> Meetings { get; } = new();
        public Mock<IExternalCalendarConnectionRepository> Connections { get; } = new();
        public Mock<ICalendarConnectionTokenProvider> Tokens { get; } = new();
        public Mock<IZoomMeetingClient> Zoom { get; } = new();
        public Mock<ICalendarEventMeetingAttendanceRepository> Attendances { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public List<CalendarEventMeeting> Due { get; } = [];

        public CalendarEventMeeting AddDue(TimeSpan endedAgo, string externalId = "zoom-meeting-1")
        {
            var meeting = new CalendarEventMeeting
            {
                Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = Guid.NewGuid(), ExternalCalendarConnectionId = Guid.NewGuid(),
                Provider = CalendarEventMeetingProviders.Zoom, ExternalMeetingId = externalId,
                JoinUrl = "https://zoom.us/j/1", Status = CalendarEventMeetingStatuses.Active
            };
            Due.Add(meeting);
            _ends[meeting.Id] = Now - endedAgo;
            var connection = new ExternalCalendarConnection
            {
                Id = meeting.ExternalCalendarConnectionId, TenantId = TenantId, UserId = Guid.NewGuid(),
                Provider = CalendarExternalSources.Zoom, ExternalAccountEmail = "me@acme.com",
                RefreshTokenEncrypted = [1], Status = ExternalCalendarConnectionStatuses.Active
            };
            Connections.Setup(c => c.GetByIdForTenantAsync(TenantId, connection.Id, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
            Tokens.Setup(t => t.GetFreshAccessTokenAsync(connection, "zoom", It.IsAny<CancellationToken>())).ReturnsAsync("access-token");
            return meeting;
        }

        private readonly Dictionary<Guid, DateTimeOffset> _ends = [];

        public async Task RunAsync()
        {
            Meetings.Setup(m => m.GetDueForAttendanceSyncAsync(TenantId, CalendarEventMeetingProviders.Zoom, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Due.Select(m => new DueAttendanceSync(m, _ends[m.Id])).ToList());
            var tenants = new Mock<ITenantRepository>();
            tenants.Setup(t => t.ListAsync(TenantStatus.Active, null, 0, 100, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Tenant> { new() { Id = TenantId, Name = "Acme", Slug = "acme", Status = TenantStatus.Active } });
            tenants.Setup(t => t.ListAsync(TenantStatus.Active, null, 100, 100, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Tenant>());
            var clock = new Mock<IDateTimeProvider>();
            clock.SetupGet(c => c.UtcNow).Returns(Now);

            var services = new ServiceCollection();
            services.AddSingleton(tenants.Object);
            services.AddSingleton(Meetings.Object);
            services.AddSingleton(Connections.Object);
            services.AddSingleton(Tokens.Object);
            services.AddSingleton(Zoom.Object);
            services.AddSingleton(Attendances.Object);
            services.AddSingleton(UnitOfWork.Object);
            services.AddSingleton(clock.Object);
            services.AddSingleton(Mock.Of<IWritableTenantContext>());
            services.AddSingleton(Mock.Of<ITenantContextSwitcher>());
            await new ZoomAttendanceSyncJob(services.BuildServiceProvider(), NullLogger<ZoomAttendanceSyncJob>.Instance)
                .RunOnceAsync(CancellationToken.None);
        }
    }

    private static ZoomAttendanceRecordDto Record(string name) => new(name, $"{name}@acme.com", Now.AddHours(-2), Now.AddHours(-1));

    [Fact]
    public async Task AtLeastOneParticipant_IsSynced()
    {
        var rig = new Rig();
        var meeting = rig.AddDue(TimeSpan.FromHours(1));
        rig.Zoom.Setup(z => z.GetAttendanceAsync("access-token", "zoom-meeting-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Record("ada")]);

        await rig.RunAsync();

        Assert.Equal(CalendarAttendanceSyncStatuses.Synced, meeting.AttendanceSyncStatus);
    }

    [Fact]
    public async Task ZeroParticipants_WithinTheCutOff_ChangesNothing()
    {
        var rig = new Rig();
        var meeting = rig.AddDue(TimeSpan.FromHours(2));
        rig.Zoom.Setup(z => z.GetAttendanceAsync("access-token", "zoom-meeting-1", It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await rig.RunAsync();

        Assert.Null(meeting.AttendanceSyncStatus);
    }

    [Fact]
    public async Task ZeroParticipants_PastTheCutOff_IsUnavailable_NotNobodyJoined()
    {
        var rig = new Rig();
        var meeting = rig.AddDue(TimeSpan.FromHours(25));
        rig.Zoom.Setup(z => z.GetAttendanceAsync("access-token", "zoom-meeting-1", It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await rig.RunAsync();

        Assert.Equal(CalendarAttendanceSyncStatuses.Unavailable, meeting.AttendanceSyncStatus);
        rig.Attendances.Verify(a => a.AddRangeAsync(It.IsAny<IEnumerable<CalendarEventMeetingAttendance>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FetchThrows_PastTheCutOff_IsSyncFailed()
    {
        var rig = new Rig();
        var meeting = rig.AddDue(TimeSpan.FromHours(30));
        rig.Zoom.Setup(z => z.GetAttendanceAsync("access-token", "zoom-meeting-1", It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("500"));

        await rig.RunAsync();

        Assert.Equal(CalendarAttendanceSyncStatuses.Failed, meeting.AttendanceSyncStatus);
    }

    [Fact]
    public async Task MissingConnection_PastTheCutOff_IsSyncFailed()
    {
        var rig = new Rig();
        var meeting = rig.AddDue(TimeSpan.FromHours(30));
        rig.Connections.Setup(c => c.GetByIdForTenantAsync(TenantId, meeting.ExternalCalendarConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarConnection?)null);

        await rig.RunAsync();

        Assert.Equal(CalendarAttendanceSyncStatuses.Failed, meeting.AttendanceSyncStatus);
    }
}
