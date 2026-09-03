using MediatR;
using Moq;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.EndBreak;
using ONEVO.Application.Features.TimeAttendance.Commands.StartBreak;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class BreakCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid EmployeeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid LegalEntityId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateOnly WorkDate = new(2026, 8, 21);
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 21, 17, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LocalNow = new(2026, 8, 21, 23, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly AttendanceLocalDayWindow LocalDayWindow = new(
        new DateTimeOffset(2026, 8, 20, 18, 30, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 21, 18, 30, 0, TimeSpan.Zero));

    [Fact]
    public async Task StartBreak_SucceedsPersistsProviderTimeAndUsesLegalEntityLocalDay()
    {
        var fixture = CreateFixture();
        var record = ActiveAttendance();
        BreakRecord? added = null;
        fixture.Attendance
            .Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        fixture.Attendance
            .Setup(x => x.AddBreakAsync(It.IsAny<BreakRecord>(), It.IsAny<CancellationToken>()))
            .Callback<BreakRecord, CancellationToken>((value, _) => added = value)
            .Returns(Task.CompletedTask);

        var result = await fixture.StartBreak.Handle(new StartBreakCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(fixture.TodayResponse, result.Value);
        Assert.NotNull(added);
        Assert.NotEqual(Guid.Empty, added!.Id);
        Assert.Equal(TenantId, added.TenantId);
        Assert.Equal(EmployeeId, added.EmployeeId);
        Assert.Equal(UtcNow, added.BreakStart);
        Assert.Null(added.BreakEnd);
        Assert.Null(added.BreakType);
        Assert.False(added.AutoDetected);
        Assert.Equal(UtcNow, added.CreatedAt);
        fixture.Attendance.Verify(x => x.SumCompletedBreakMinutesAsync(
            TenantId, EmployeeId, LocalDayWindow.Start, LocalDayWindow.End, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Attendance.Verify(x => x.GetOpenBreakTrackedAsync(
            TenantId, EmployeeId, LocalDayWindow.Start, LocalDayWindow.End, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Attendance.Verify(x => x.GetAnyOpenBreakTrackedAsync(
            TenantId, EmployeeId, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Attendance.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartBreak_DoesNotRevertApprovedExpectedWorkAreaSnapshotToLiveContextArea()
    {
        // Today's live context happens to resolve to "onsite" (e.g. a later date's fallback),
        // while the attendance record was already snapshotted as "remote" at clock-in. Start
        // Break must not re-resolve or overwrite that persisted snapshot.
        var fixture = CreateFixture(expectedWorkArea: AttendanceRecord.WorkAreaOnsite);
        var record = ActiveAttendance();
        fixture.Attendance
            .Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        fixture.Attendance
            .Setup(x => x.AddBreakAsync(It.IsAny<BreakRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await fixture.StartBreak.Handle(new StartBreakCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttendanceRecord.WorkAreaRemote, record.ExpectedWorkArea);
    }

    [Fact]
    public async Task StartBreak_RejectsWhenCurrentEmployeeIsMissing()
    {
        var fixture = CreateFixture();
        fixture.TodayState
            .Setup(x => x.ResolveContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayContext>.NotFound("Current employee record was not found."));

        var result = await fixture.StartBreak.Handle(new StartBreakCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        fixture.UnitOfWork.Verify(x => x.ExecuteInTransactionAsync(
            It.IsAny<Func<CancellationToken, Task<Result<bool>>>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartBreak_RejectsWhenAllowanceIsNull()
    {
        var fixture = CreateFixture(allowance: null);

        var result = await fixture.StartBreak.Handle(new StartBreakCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("break_allowance_not_configured", result.Error);
    }

    [Fact]
    public async Task StartBreak_RejectsWhenAllowanceIsZeroOrAlreadyUsed()
    {
        var zeroFixture = CreateFixture(allowance: 0);
        var zeroResult = await zeroFixture.StartBreak.Handle(new StartBreakCommand(), CancellationToken.None);

        Assert.False(zeroResult.IsSuccess);
        Assert.Equal("break_allowance_used", zeroResult.Error);

        var usedFixture = CreateFixture(allowance: 60);
        usedFixture.Attendance
            .Setup(x => x.SumCompletedBreakMinutesAsync(
                TenantId, EmployeeId, LocalDayWindow.Start, LocalDayWindow.End, It.IsAny<CancellationToken>()))
            .ReturnsAsync(60);
        var usedResult = await usedFixture.StartBreak.Handle(new StartBreakCommand(), CancellationToken.None);

        Assert.False(usedResult.IsSuccess);
        Assert.Equal("break_allowance_used", usedResult.Error);
        usedFixture.Attendance.Verify(x => x.AddBreakAsync(
            It.IsAny<BreakRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartBreak_RejectsWhenAttendanceIsMissingOrNotStarted()
    {
        var missingFixture = CreateFixture();
        missingFixture.Attendance
            .Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AttendanceRecord?)null);
        var missingResult = await missingFixture.StartBreak.Handle(new StartBreakCommand(), CancellationToken.None);

        Assert.False(missingResult.IsSuccess);
        Assert.Equal("not_clocked_in", missingResult.Error);

        var notStartedFixture = CreateFixture();
        notStartedFixture.Attendance
            .Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendanceRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, Date = WorkDate });
        var notStartedResult = await notStartedFixture.StartBreak.Handle(new StartBreakCommand(), CancellationToken.None);

        Assert.False(notStartedResult.IsSuccess);
        Assert.Equal("not_clocked_in", notStartedResult.Error);
    }

    [Fact]
    public async Task StartBreak_RejectsWhenAttendanceIsAlreadyClockedOut()
    {
        var fixture = CreateFixture();
        fixture.Attendance
            .Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendanceRecord
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                EmployeeId = EmployeeId,
                Date = WorkDate,
                ActualStart = UtcNow.AddHours(-2),
                ActualEnd = UtcNow.AddHours(-1)
            });

        var result = await fixture.StartBreak.Handle(new StartBreakCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("already_clocked_out", result.Error);
    }

    [Fact]
    public async Task StartBreak_RejectsWhenAnOpenBreakExistsInCurrentOrHistoricalDay()
    {
        var currentFixture = CreateFixture();
        currentFixture.Attendance
            .Setup(x => x.GetOpenBreakTrackedAsync(
                TenantId, EmployeeId, LocalDayWindow.Start, LocalDayWindow.End, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BreakRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, BreakStart = UtcNow.AddMinutes(-10) });
        var currentResult = await currentFixture.StartBreak.Handle(new StartBreakCommand(), CancellationToken.None);

        Assert.False(currentResult.IsSuccess);
        Assert.Equal("break_already_active", currentResult.Error);

        var historicalFixture = CreateFixture();
        historicalFixture.Attendance
            .Setup(x => x.GetAnyOpenBreakTrackedAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BreakRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, BreakStart = LocalDayWindow.Start.AddDays(-1) });
        var historicalResult = await historicalFixture.StartBreak.Handle(new StartBreakCommand(), CancellationToken.None);

        Assert.False(historicalResult.IsSuccess);
        Assert.Equal("break_already_active", historicalResult.Error);
    }

    [Fact]
    public async Task StartBreak_MapsDuplicateOpenBreakRaceToConflict()
    {
        var fixture = CreateFixture();
        fixture.Attendance
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UniqueConstraintConflictException(new InvalidOperationException()));

        var result = await fixture.StartBreak.Handle(new StartBreakCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("break_already_active", result.Error);
    }

    [Fact]
    public async Task EndBreak_SucceedsSetsProviderTimeAndRecalculatesBreakMinutes()
    {
        var fixture = CreateFixture();
        var record = ActiveAttendance();
        var openBreak = new BreakRecord
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            EmployeeId = EmployeeId,
            BreakStart = UtcNow.AddMinutes(-30),
            BreakEnd = null
        };
        fixture.Attendance
            .Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        fixture.Attendance
            .Setup(x => x.GetOpenBreakTrackedAsync(
                TenantId, EmployeeId, LocalDayWindow.Start, LocalDayWindow.End, It.IsAny<CancellationToken>()))
            .ReturnsAsync(openBreak);
        fixture.Attendance
            .Setup(x => x.SumCompletedBreakMinutesAsync(
                TenantId, EmployeeId, LocalDayWindow.Start, LocalDayWindow.End, It.IsAny<CancellationToken>()))
            .ReturnsAsync(20);

        var result = await fixture.EndBreak.Handle(new EndBreakCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(fixture.TodayResponse, result.Value);
        Assert.Equal(UtcNow, openBreak.BreakEnd);
        Assert.Equal(50, record.BreakMinutes);
        fixture.Attendance.Verify(x => x.SumCompletedBreakMinutesAsync(
            TenantId, EmployeeId, LocalDayWindow.Start, LocalDayWindow.End, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Attendance.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EndBreak_DoesNotRevertApprovedExpectedWorkAreaSnapshotToLiveContextArea()
    {
        var fixture = CreateFixture(expectedWorkArea: AttendanceRecord.WorkAreaOnsite);
        var record = ActiveAttendance();
        var openBreak = new BreakRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId,
            BreakStart = UtcNow.AddMinutes(-30), BreakEnd = null
        };
        fixture.Attendance
            .Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        fixture.Attendance
            .Setup(x => x.GetOpenBreakTrackedAsync(
                TenantId, EmployeeId, LocalDayWindow.Start, LocalDayWindow.End, It.IsAny<CancellationToken>()))
            .ReturnsAsync(openBreak);
        fixture.Attendance
            .Setup(x => x.SumCompletedBreakMinutesAsync(
                TenantId, EmployeeId, LocalDayWindow.Start, LocalDayWindow.End, It.IsAny<CancellationToken>()))
            .ReturnsAsync(20);

        var result = await fixture.EndBreak.Handle(new EndBreakCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttendanceRecord.WorkAreaRemote, record.ExpectedWorkArea);
    }

    [Fact]
    public async Task EndBreak_RejectsWhenAttendanceIsMissingNotStartedOrClockedOut()
    {
        var missingFixture = CreateFixture();
        missingFixture.Attendance
            .Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AttendanceRecord?)null);
        var missingResult = await missingFixture.EndBreak.Handle(new EndBreakCommand(), CancellationToken.None);
        Assert.False(missingResult.IsSuccess);
        Assert.Equal("not_clocked_in", missingResult.Error);

        var notStartedFixture = CreateFixture();
        notStartedFixture.Attendance
            .Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendanceRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, Date = WorkDate });
        var notStartedResult = await notStartedFixture.EndBreak.Handle(new EndBreakCommand(), CancellationToken.None);
        Assert.False(notStartedResult.IsSuccess);
        Assert.Equal("not_clocked_in", notStartedResult.Error);

        var closedFixture = CreateFixture();
        closedFixture.Attendance
            .Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendanceRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, Date = WorkDate,
                ActualStart = UtcNow.AddHours(-2), ActualEnd = UtcNow.AddHours(-1)
            });
        var closedResult = await closedFixture.EndBreak.Handle(new EndBreakCommand(), CancellationToken.None);
        Assert.False(closedResult.IsSuccess);
        Assert.Equal("already_clocked_out", closedResult.Error);
    }

    [Fact]
    public async Task EndBreak_RejectsWhenNoCurrentBreakExistsIncludingHistoricalOpenBreak()
    {
        var fixture = CreateFixture();
        fixture.Attendance
            .Setup(x => x.GetAnyOpenBreakTrackedAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BreakRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId,
                BreakStart = LocalDayWindow.Start.AddDays(-1)
            });

        var result = await fixture.EndBreak.Handle(new EndBreakCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("no_active_break", result.Error);
        fixture.Attendance.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EndBreak_RejectsWhenProviderTimePrecedesBreakStart()
    {
        var fixture = CreateFixture();
        var openBreak = new BreakRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId,
            BreakStart = UtcNow.AddMinutes(1)
        };
        fixture.Attendance
            .Setup(x => x.GetOpenBreakTrackedAsync(
                TenantId, EmployeeId, LocalDayWindow.Start, LocalDayWindow.End, It.IsAny<CancellationToken>()))
            .ReturnsAsync(openBreak);

        var result = await fixture.EndBreak.Handle(new EndBreakCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("invalid_break_time", result.Error);
        Assert.Null(openBreak.BreakEnd);
    }

    [Fact]
    public async Task EndBreak_MapsConcurrentAlreadyEndedBreakToConflict()
    {
        var fixture = CreateFixture();
        fixture.Attendance
            .Setup(x => x.GetOpenBreakTrackedAsync(
                TenantId, EmployeeId, LocalDayWindow.Start, LocalDayWindow.End, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BreakRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId,
                BreakStart = UtcNow.AddMinutes(-5)
            });
        fixture.Attendance
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException());

        var result = await fixture.EndBreak.Handle(new EndBreakCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("break_already_ended", result.Error);
    }

    private static AttendanceRecord ActiveAttendance()
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            EmployeeId = EmployeeId,
            Date = WorkDate,
            ActualStart = UtcNow.AddHours(-8),
            ExpectedWorkArea = AttendanceRecord.WorkAreaRemote,
            Status = AttendanceRecord.StatusActive
        };

    private static Fixture CreateFixture(int? allowance = 60, string expectedWorkArea = AttendanceRecord.WorkAreaRemote)
    {
        var context = new AttendanceTodayContext(
            new Employee
            {
                Id = EmployeeId,
                TenantId = TenantId,
                UserId = Guid.NewGuid(),
                LegalEntityId = LegalEntityId,
                WorkModeId = 1
            },
            new LegalEntity
            {
                Id = LegalEntityId,
                TenantId = TenantId,
                Timezone = "Asia/Colombo",
                BreakDurationMinutes = allowance
            },
            "Asia/Colombo",
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo"),
            WorkDate,
            UtcNow,
            LocalNow,
            new AttendanceSchedule("configured", true, new(9, 0), new(17, 30), 510),
            expectedWorkArea,
            "active_employee_work_mode",
            new ClockInPolicy { Id = Guid.NewGuid(), RemoteWebEnabled = true },
            "configured",
            new AllowedClockInMethods(true, false, false, false, false, null),
            LocalDayWindow);

        var todayState = new Mock<IAttendanceTodayStateService>();
        todayState
            .Setup(x => x.ResolveContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayContext>.Success(context));
        var todayResponse = CreateTodayResponse();
        todayState
            .Setup(x => x.GetTodayAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayResponse>.Success(todayResponse));

        var attendance = new Mock<IAttendanceReadRepository>();
        attendance
            .Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveAttendance());
        attendance
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result<bool>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<bool>>> operation, CancellationToken ct) => operation(ct));

        return new Fixture(
            new StartBreakCommandHandler(todayState.Object, attendance.Object, unitOfWork.Object),
            new EndBreakCommandHandler(todayState.Object, attendance.Object, unitOfWork.Object),
            todayState,
            attendance,
            unitOfWork,
            todayResponse);
    }

    private static AttendanceTodayResponse CreateTodayResponse()
        => new(
            EmployeeId,
            LegalEntityId,
            WorkDate,
            "Asia/Colombo",
            "configured",
            "configured",
            true,
            false,
            null,
            "09:00",
            "17:30",
            510,
            60,
            0,
            60,
            "ended",
            [],
            "remote",
            AttendanceRecord.StatusActive,
            UtcNow.AddHours(-8),
            null,
            0,
            "web",
            false,
            true,
            true,
            false,
            false,
            false,
            new AllowedClockInMethods(true, false, false, false, false, null),
            []);

    private sealed record Fixture(
        StartBreakCommandHandler StartBreak,
        EndBreakCommandHandler EndBreak,
        Mock<IAttendanceTodayStateService> TodayState,
        Mock<IAttendanceReadRepository> Attendance,
        Mock<IUnitOfWork> UnitOfWork,
        AttendanceTodayResponse TodayResponse);
}
