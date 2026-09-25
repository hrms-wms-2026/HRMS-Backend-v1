using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetMyWorkPattern;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Services;
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
    private readonly Mock<IAttendanceTodayStateService> _todayState = new();

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
        _todayState.Setup(x => x.GetTodayAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayResponse>.Success(BuildTodayResponse([])));

        return new GetMyWorkPatternQueryHandler(
            _currentUser.Object, _dateTime.Object, _employees.Object, _summaries.Object, _snapshots.Object,
            _meetings.Object, _todayState.Object);
    }

    private static AttendanceTodayResponse BuildTodayResponse(IReadOnlyList<AttendanceTodayBreakInterval> breaks) => new(
        EmployeeId, Guid.NewGuid(), Today, "UTC", "configured", "configured", true,
        IsHoliday: false, HolidayName: null, ScheduledStartTime: null, ScheduledEndTime: null,
        RequiredWorkMinutes: null, BreakAllowanceMinutes: null, BreakUsedMinutes: 0, BreakRemainingMinutes: null,
        BreakState: "not_started", Breaks: breaks, ExpectedWorkMode: null, AttendanceStatus: "clocked_in",
        ClockInAt: null, ClockOutAt: null, TotalWorkedMinutes: 0, AttendanceSource: null,
        CanClockIn: false, CanClockOut: true, CanStartBreak: true, CanEndBreak: false,
        ShouldHaveClockedIn: false, CanViewCoveredEmployees: false,
        AllowedClockInMethods: new AllowedClockInMethods(true, false, false, false, false, null),
        Messages: []);

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);
        var sut = new GetMyWorkPatternQueryHandler(
            _currentUser.Object, _dateTime.Object, _employees.Object, _summaries.Object, _snapshots.Object,
            _meetings.Object, _todayState.Object);

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
            _currentUser.Object, _dateTime.Object, _employees.Object, _summaries.Object, _snapshots.Object,
            _meetings.Object, _todayState.Object);

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
                    TotalActiveMinutes = 400, FocusMinutes = 180, TotalMeetingMinutes = 60,
                    TotalIdleMinutes = 45, CreatedAt = DateTimeOffset.UtcNow
                }
            ]);

        var result = await sut.Handle(new GetMyWorkPatternQuery(pastDay, pastDay), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var day = result.Value!.Days.Should().ContainSingle().Subject;
        day.FocusMinutes.Should().Be(180);
        day.MeetingMinutes.Should().Be(60);
        day.AdminMinutes.Should().Be(160); // 400 - 180 - 60
        day.IdleMinutes.Should().Be(45);
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
            .Append(new ActivitySnapshot
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(),
                CapturedAt = baseTime.AddMinutes(35), ActiveSeconds = 0, IdleSeconds = 300,
                ForegroundProcessName = null, CreatedAt = baseTime
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
        day.IdleMinutes.Should().Be(5); // one trailing 300s-idle snapshot
        _summaries.Verify(x => x.GetRangeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Regression: the tray keeps sampling straight through a break, so an unfiltered snapshot/
    // meeting-signal stream could report more Admin/Focus/Meeting minutes than the employee was
    // ever "Worked" for (which explicitly subtracts break time) - the Work Pattern card then
    // rendered as if 100%+ of worked time were spent on one category. Samples captured during an
    // open or closed break must be excluded.
    [Fact]
    public async Task Handle_Today_ExcludesSnapshotsAndMeetingSignalsCapturedDuringABreak()
    {
        var sut = BuildSut();
        var baseTime = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        var breakStart = baseTime.AddMinutes(10);
        var breakEnd = baseTime.AddMinutes(15);
        _todayState.Setup(x => x.GetTodayAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayResponse>.Success(
                BuildTodayResponse([new AttendanceTodayBreakInterval(breakStart, breakEnd)])));

        var snaps = new List<ActivitySnapshot>
        {
            // Before the break - counts.
            new()
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(),
                CapturedAt = baseTime.AddMinutes(5), ActiveSeconds = 300, IdleSeconds = 0,
                ForegroundProcessName = "code.exe", CreatedAt = baseTime
            },
            // Inside the break window - must be excluded even though the tray still captured it.
            new()
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(),
                CapturedAt = baseTime.AddMinutes(12), ActiveSeconds = 180, IdleSeconds = 0,
                ForegroundProcessName = "code.exe", CreatedAt = baseTime
            },
            // After the break - counts.
            new()
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(),
                CapturedAt = baseTime.AddMinutes(20), ActiveSeconds = 300, IdleSeconds = 0,
                ForegroundProcessName = "code.exe", CreatedAt = baseTime
            }
        };
        _snapshots.Setup(x => x.GetAllByEmployeeDateAsync(TenantId, EmployeeId, Today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snaps);
        _meetings.Setup(x => x.GetAllByEmployeeDateAsync(TenantId, EmployeeId, Today, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                // Inside the break - must be excluded.
                new MeetingSignal
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(),
                    CapturedAt = baseTime.AddMinutes(13), IsMeetingAppRunning = true, CreatedAt = baseTime
                },
                // Outside the break - counts.
                new MeetingSignal
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(),
                    CapturedAt = baseTime.AddMinutes(20), IsMeetingAppRunning = true, CreatedAt = baseTime
                }
            ]);

        var result = await sut.Handle(new GetMyWorkPatternQuery(Today, Today), CancellationToken.None);

        var day = result.Value!.Days.Should().ContainSingle().Subject;
        // Only the non-break meeting sample counts.
        day.MeetingMinutes.Should().Be(2);
        // 10 active minutes from the two non-break snapshots (the 3-minute break-time snapshot
        // is excluded), minus the 2 meeting minutes that share the same active window = 8.
        day.AdminMinutes.Should().Be(8);
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
