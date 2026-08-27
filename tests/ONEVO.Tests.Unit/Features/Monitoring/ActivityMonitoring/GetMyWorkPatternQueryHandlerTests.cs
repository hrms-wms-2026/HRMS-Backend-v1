using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetMyWorkPattern;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

public sealed class GetMyWorkPatternQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 8, 27);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _dateTime = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IActivityDailySummaryRepository> _summaries = new();
    private readonly Mock<IActivitySnapshotRepository> _snapshots = new();
    private readonly Mock<IMeetingSignalRepository> _meetings = new();

    private GetMyWorkPatternQueryHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _dateTime.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 27, 15, 0, 0, TimeSpan.Zero));
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, UserId = UserId, TenantId = TenantId });
        _snapshots.Setup(x => x.GetAllByEmployeeDateAsync(TenantId, EmployeeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _meetings.Setup(x => x.GetAllByEmployeeDateAsync(TenantId, EmployeeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        return new GetMyWorkPatternQueryHandler(
            _currentUser.Object, _dateTime.Object, _employees.Object, _summaries.Object, _snapshots.Object, _meetings.Object);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);
        var sut = new GetMyWorkPatternQueryHandler(
            _currentUser.Object, _dateTime.Object, _employees.Object, _summaries.Object, _snapshots.Object, _meetings.Object);

        var result = await sut.Handle(new GetMyWorkPatternQuery(Today, Today), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_NoEmployeeRecord_ReturnsForbidden()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);
        var sut = new GetMyWorkPatternQueryHandler(
            _currentUser.Object, _dateTime.Object, _employees.Object, _summaries.Object, _snapshots.Object, _meetings.Object);

        var result = await sut.Handle(new GetMyWorkPatternQuery(Today, Today), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_PastDayUsesAggregatedSummaryAndDerivesAdminMinutes()
    {
        var sut = BuildSut();
        var pastDay = Today.AddDays(-2);
        _summaries.Setup(x => x.GetRangeAsync(TenantId, EmployeeId, pastDay, pastDay, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ActivityDailySummary
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, Date = pastDay,
                    TotalActiveMinutes = 400, FocusMinutes = 180, TotalMeetingMinutes = 60, CreatedAt = DateTimeOffset.UtcNow
                }
            ]);

        var result = await sut.Handle(new GetMyWorkPatternQuery(pastDay, pastDay), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var day = result.Value!.Days.Should().ContainSingle().Subject;
        day.FocusMinutes.Should().Be(180);
        day.MeetingMinutes.Should().Be(60);
        day.AdminMinutes.Should().Be(160); // 400 - 180 - 60
    }

    [Fact]
    public async Task Handle_PastDayWithNoSummaryRow_DefaultsToAllZero()
    {
        var sut = BuildSut();
        var pastDay = Today.AddDays(-1);
        _summaries.Setup(x => x.GetRangeAsync(TenantId, EmployeeId, pastDay, pastDay, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await sut.Handle(new GetMyWorkPatternQuery(pastDay, pastDay), CancellationToken.None);

        var day = result.Value!.Days.Should().ContainSingle().Subject;
        day.FocusMinutes.Should().Be(0);
        day.MeetingMinutes.Should().Be(0);
        day.AdminMinutes.Should().Be(0);
    }

    [Fact]
    public async Task Handle_FutureDay_IsAlwaysZero()
    {
        var sut = BuildSut();
        var futureDay = Today.AddDays(3);

        var result = await sut.Handle(new GetMyWorkPatternQuery(futureDay, futureDay), CancellationToken.None);

        var day = result.Value!.Days.Should().ContainSingle().Subject;
        day.FocusMinutes.Should().Be(0);
        day.MeetingMinutes.Should().Be(0);
        day.AdminMinutes.Should().Be(0);
        _summaries.Verify(x => x.GetRangeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Today_ComputesLiveFromSnapshotsAndMeetingSignals()
    {
        var sut = BuildSut();
        var baseTime = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        // Six 5-minute active snapshots in the same app = 30 contiguous active minutes -> focus.
        var snaps = Enumerable.Range(0, 6)
            .Select(i => new ActivitySnapshot
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(),
                CapturedAt = baseTime.AddMinutes((i + 1) * 5), ActiveSeconds = 300, IdleSeconds = 0,
                ForegroundProcessName = "code.exe", CreatedAt = baseTime
            })
            .ToList();
        _snapshots.Setup(x => x.GetAllByEmployeeDateAsync(TenantId, EmployeeId, Today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snaps);
        _meetings.Setup(x => x.GetAllByEmployeeDateAsync(TenantId, EmployeeId, Today, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MeetingSignal { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(), CapturedAt = baseTime, IsMeetingAppRunning = true, CreatedAt = baseTime },
                new MeetingSignal { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(), CapturedAt = baseTime, IsMeetingAppRunning = false, CreatedAt = baseTime }
            ]);

        var result = await sut.Handle(new GetMyWorkPatternQuery(Today, Today), CancellationToken.None);

        var day = result.Value!.Days.Should().ContainSingle().Subject;
        day.FocusMinutes.Should().Be(30);
        day.MeetingMinutes.Should().Be(2); // 1 meeting sample * 2 min/sample
        day.AdminMinutes.Should().Be(0); // 30 active minutes total, all accounted for by focus
        _summaries.Verify(x => x.GetRangeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_OverlappingFocusAndMeetingMinutes_ClampsAdminAtZero()
    {
        var sut = BuildSut();
        var pastDay = Today.AddDays(-1);
        _summaries.Setup(x => x.GetRangeAsync(TenantId, EmployeeId, pastDay, pastDay, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ActivityDailySummary
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, Date = pastDay,
                    TotalActiveMinutes = 100, FocusMinutes = 80, TotalMeetingMinutes = 60, CreatedAt = DateTimeOffset.UtcNow
                }
            ]);

        var result = await sut.Handle(new GetMyWorkPatternQuery(pastDay, pastDay), CancellationToken.None);

        result.Value!.Days[0].AdminMinutes.Should().Be(0); // would be -40 unclamped
    }

    [Fact]
    public async Task Handle_MultiDayRange_ReturnsOneEntryPerDayInOrder()
    {
        var sut = BuildSut();
        var from = Today.AddDays(-2);
        var to = Today;
        _summaries.Setup(x => x.GetRangeAsync(TenantId, EmployeeId, from, Today.AddDays(-1), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await sut.Handle(new GetMyWorkPatternQuery(from, to), CancellationToken.None);

        result.Value!.Days.Should().HaveCount(3);
        result.Value.Days.Select(d => d.Date).Should().Equal(from, from.AddDays(1), to);
    }
}
