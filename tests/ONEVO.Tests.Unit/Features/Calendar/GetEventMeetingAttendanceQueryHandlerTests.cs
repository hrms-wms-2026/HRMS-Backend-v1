using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Queries.GetEventMeetingAttendance;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class GetEventMeetingAttendanceQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventMeetingRepository> _meetings = new();
    private readonly Mock<ICalendarEventMeetingAttendanceRepository> _attendances = new();

    private GetEventMeetingAttendanceQueryHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        return new GetEventMeetingAttendanceQueryHandler(_currentUser.Object, _meetings.Object, _attendances.Object);
    }

    [Fact]
    public async Task Handle_NoMeeting_ReturnsEmptyUnavailableResult()
    {
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEventMeeting?)null);

        var result = await BuildSut().Handle(new GetEventMeetingAttendanceQuery(EventId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Available.Should().BeFalse();
        result.Value.Attendees.Should().BeEmpty();
        result.Value.SyncStatus.Should().Be("none");
    }

    [Fact]
    public async Task Handle_MeetingNeverSynced_ReturnsUnavailable()
    {
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting { Id = Guid.NewGuid(), LastAttendanceSyncedAt = null });

        var result = await BuildSut().Handle(new GetEventMeetingAttendanceQuery(EventId), CancellationToken.None);

        result.Value!.Available.Should().BeFalse();
        result.Value!.SyncStatus.Should().Be("pending");
    }

    [Fact]
    public async Task Handle_MeetingSynced_ReturnsAttendees()
    {
        var meeting = new CalendarEventMeeting { Id = Guid.NewGuid(), LastAttendanceSyncedAt = DateTimeOffset.UtcNow, AttendanceSyncStatus = CalendarAttendanceSyncStatuses.Synced };
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(meeting);
        _attendances.Setup(a => a.GetByMeetingIdAsync(TenantId, meeting.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new CalendarEventMeetingAttendance
                {
                    Id = Guid.NewGuid(), ExternalParticipantName = "Ada Lovelace", ExternalParticipantEmail = "ada@example.com",
                    JoinedAt = DateTimeOffset.UtcNow.AddMinutes(-30), LeftAt = DateTimeOffset.UtcNow, DurationSeconds = 1800
                }
            ]);

        var result = await BuildSut().Handle(new GetEventMeetingAttendanceQuery(EventId), CancellationToken.None);

        result.Value!.Available.Should().BeTrue();
        result.Value.Attendees.Should().ContainSingle(a => a.Name == "Ada Lovelace");
        result.Value.SyncStatus.Should().Be("synced");
    }

    [Theory]
    [InlineData(CalendarAttendanceSyncStatuses.Unavailable)]
    [InlineData(CalendarAttendanceSyncStatuses.Failed)]
    public async Task Handle_Undecidable_IsNotAvailable_AndSaysWhy(string status)
    {
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting { Id = Guid.NewGuid(), AttendanceSyncStatus = status });

        var result = await BuildSut().Handle(new GetEventMeetingAttendanceQuery(EventId), CancellationToken.None);

        result.Value!.Available.Should().BeFalse();
        result.Value.SyncStatus.Should().Be(status);
    }
}
