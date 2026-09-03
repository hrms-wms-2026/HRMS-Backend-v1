using Moq;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.ClockIn;
using ONEVO.Application.Features.TimeAttendance.Commands.ClockOut;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class ClockInOutCommandHandlerTests
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
    public async Task ClockIn_CreatesLateRecordUsingLegalEntityLocalDateAndReturnsToday()
    {
        var fixture = CreateFixture();
        AttendanceRecord? added = null;
        fixture.Attendance
            .Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AttendanceRecord?)null);
        fixture.Attendance
            .Setup(x => x.AddRecordAsync(It.IsAny<AttendanceRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AttendanceRecord, CancellationToken>((record, _) => added = record)
            .Returns(Task.CompletedTask);

        var result = await fixture.ClockIn.Handle(new ClockInCommand("web"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Equal(WorkDate, added!.Date);
        Assert.True(added.ExpectedWorkingDay);
        Assert.Equal(AttendanceRecord.WorkTimeTypeFixed, added.WorkTimeType);
        Assert.Equal(new TimeOnly(9, 0), added.ScheduledStart);
        Assert.Equal(new TimeOnly(17, 30), added.ScheduledEnd);
        Assert.Equal(510, added.RequiredWorkMinutes);
        Assert.Equal(AttendanceRecord.WorkAreaRemote, added.ExpectedWorkArea);
        Assert.Equal("Asia/Colombo", added.ScheduleTimezone);
        Assert.Equal(UtcNow, added.ActualStart);
        Assert.Equal(840, added.LateMinutes);
        Assert.Equal(AttendanceRecord.StatusLate, added.Status);
        Assert.Equal("web", added.AttendanceSource);
        fixture.TodayState.Verify(x => x.GetTodayAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClockIn_RejectsWhenCurrentEmployeeIsMissing()
    {
        var fixture = CreateFixture();
        fixture.TodayState
            .Setup(x => x.ResolveContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayContext>.NotFound("Current employee record was not found."));

        var result = await fixture.ClockIn.Handle(new ClockInCommand("web"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        fixture.UnitOfWork.Verify(x => x.ExecuteInTransactionAsync(
            It.IsAny<Func<CancellationToken, Task<Result<bool>>>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ClockIn_RejectsWhenScheduleIsNotConfigured()
    {
        var fixture = CreateFixture(
            schedule: new AttendanceSchedule("not_configured", false, null, null, null));

        var result = await fixture.ClockIn.Handle(new ClockInCommand("web"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("schedule_not_configured", result.Error);
        fixture.Attendance.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ClockIn_RejectsWhenPolicyIsNotConfigured()
    {
        var fixture = CreateFixture(policyStatus: "not_configured");

        var result = await fixture.ClockIn.Handle(new ClockInCommand("web"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("clock_in_policy_not_configured", result.Error);
    }

    [Fact]
    public async Task ClockIn_RejectsWhenMultiplePoliciesAreActive()
    {
        var fixture = CreateFixture(policyStatus: "configuration_conflict");

        var result = await fixture.ClockIn.Handle(new ClockInCommand("web"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("multiple_active_company_policies", result.Error);
    }

    [Fact]
    public async Task ClockIn_RejectsAlreadyClockedInAndAlreadyClockedOut()
    {
        var fixture = CreateFixture();
        var active = new AttendanceRecord
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            EmployeeId = EmployeeId,
            Date = WorkDate,
            ActualStart = UtcNow.AddHours(-1)
        };
        fixture.Attendance
            .Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(active);

        var activeResult = await fixture.ClockIn.Handle(new ClockInCommand("web"), CancellationToken.None);

        Assert.False(activeResult.IsSuccess);
        Assert.Equal("already_clocked_in", activeResult.Error);

        active.ActualEnd = UtcNow;
        var closedResult = await fixture.ClockIn.Handle(new ClockInCommand("web"), CancellationToken.None);

        Assert.False(closedResult.IsSuccess);
        Assert.Equal("already_clocked_out", closedResult.Error);
    }

    [Fact]
    public async Task ClockIn_ApprovedOnsiteOverride_PersistsOnsiteAsEffectiveExpectedAreaAndAllowsWebWhenOnsiteEnabled()
    {
        // The employee's permanent work mode is remote (encoded upstream, before reaching this
        // handler), but AttendanceTodayStateService already resolved today's effective area to
        // the approved onsite override; Onsite web is enabled while Remote web is disabled for
        // this policy, proving the handler uses the resolved override, not the permanent mode.
        var fixture = CreateFixture(
            expectedWorkArea: AttendanceRecord.WorkAreaOnsite,
            expectedWorkAreaSource: "approved_work_area_change_request",
            allowedMethods: new AllowedClockInMethods(true, false, false, true, false, null));
        AttendanceRecord? added = null;
        fixture.Attendance
            .Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AttendanceRecord?)null);
        fixture.Attendance
            .Setup(x => x.AddRecordAsync(It.IsAny<AttendanceRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AttendanceRecord, CancellationToken>((record, _) => added = record)
            .Returns(Task.CompletedTask);

        var result = await fixture.ClockIn.Handle(new ClockInCommand("web"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Equal(AttendanceRecord.WorkAreaOnsite, added!.ExpectedWorkArea);
    }

    [Fact]
    public async Task ClockIn_ApprovedOnsiteOverride_RejectedWhenOnsiteWebIsDisabledEvenThoughRemoteWebWasEnabled()
    {
        // Inverse of the override case above: the effective area for today is the approved
        // onsite override, and the policy disallows web for onsite, so the clock-in must be
        // rejected even though this same policy happens to allow web for remote.
        var fixture = CreateFixture(
            expectedWorkArea: AttendanceRecord.WorkAreaOnsite,
            expectedWorkAreaSource: "approved_work_area_change_request",
            allowedMethods: new AllowedClockInMethods(false, false, false, false, false, null));

        var result = await fixture.ClockIn.Handle(new ClockInCommand("web"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        fixture.Attendance.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ClockInValidator_RejectsUnsupportedSource()
    {
        var result = new ClockInCommandValidator().Validate(new ClockInCommand("desktop"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage == "Source must be web.");
    }

    [Fact]
    public async Task ClockIn_AllowsConfiguredNonWorkingDayAndPersistsAccurateWorkingDayState()
    {
        var fixture = CreateFixture(schedule: new AttendanceSchedule("configured", false, new(9, 0), new(17, 30), 510));
        AttendanceRecord? added = null;
        fixture.Attendance
            .Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AttendanceRecord?)null);
        fixture.Attendance
            .Setup(x => x.AddRecordAsync(It.IsAny<AttendanceRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AttendanceRecord, CancellationToken>((record, _) => added = record)
            .Returns(Task.CompletedTask);

        var result = await fixture.ClockIn.Handle(new ClockInCommand("web"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.False(added!.ExpectedWorkingDay);
        fixture.Attendance.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClockIn_RejectsWhenWebIsNotAllowedByWorkModePolicy()
    {
        var fixture = CreateFixture(
            allowedMethods: new AllowedClockInMethods(false, false, false, false, false, null));

        var result = await fixture.ClockIn.Handle(new ClockInCommand("web"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Contains("not allowed", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClockIn_MapsDuplicateRecordRaceToConflict()
    {
        var fixture = CreateFixture();
        fixture.Attendance
            .Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AttendanceRecord?)null);
        fixture.Attendance
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UniqueConstraintConflictException(new InvalidOperationException()));

        var result = await fixture.ClockIn.Handle(new ClockInCommand("web"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("just created", result.Error!);
    }

    [Fact]
    public async Task ClockOut_SubtractsCompletedBreakMinutesUsingLocalDayWindow()
    {
        var fixture = CreateFixture();
        var record = new AttendanceRecord
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            EmployeeId = EmployeeId,
            Date = WorkDate,
            ActualStart = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero),
            RequiredWorkMinutes = 480,
            Status = AttendanceRecord.StatusActive
        };
        fixture.Attendance
            .Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        fixture.Attendance
            .Setup(x => x.SumCompletedBreakMinutesAsync(
                TenantId, EmployeeId, LocalDayWindow.Start, LocalDayWindow.End, It.IsAny<CancellationToken>()))
            .ReturnsAsync(30);

        var result = await fixture.ClockOut.Handle(new ClockOutCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UtcNow, record.ActualEnd);
        Assert.Equal(30, record.BreakMinutes);
        Assert.Equal(480, record.WorkedMinutes);
        Assert.Equal(AttendanceRecord.StatusShortHours, record.Status);
        fixture.Attendance.Verify(x => x.HasOpenBreakAsync(
            TenantId, EmployeeId, LocalDayWindow.Start, LocalDayWindow.End, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Attendance.Verify(x => x.SumCompletedBreakMinutesAsync(
            TenantId, EmployeeId, LocalDayWindow.Start, LocalDayWindow.End, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClockOut_DoesNotRevertApprovedExpectedWorkAreaSnapshotToLivePermanentWorkMode()
    {
        // The attendance snapshot was persisted as "remote" (an approved override) at clock-in
        // time. Today's live context now happens to resolve to "onsite" (e.g. the override
        // expired for a later date, or the fallback would differ) - Clock Out must not re-resolve
        // or overwrite the already-persisted snapshot.
        var fixture = CreateFixture(expectedWorkArea: AttendanceRecord.WorkAreaOnsite);
        var record = new AttendanceRecord
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            EmployeeId = EmployeeId,
            Date = WorkDate,
            ActualStart = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero),
            ExpectedWorkArea = AttendanceRecord.WorkAreaRemote,
            RequiredWorkMinutes = 480,
            Status = AttendanceRecord.StatusActive
        };
        fixture.Attendance
            .Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var result = await fixture.ClockOut.Handle(new ClockOutCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttendanceRecord.WorkAreaRemote, record.ExpectedWorkArea);
    }

    [Fact]
    public async Task ClockOut_RejectsWhenNoAttendanceRecordExists()
    {
        var fixture = CreateFixture();
        fixture.Attendance
            .Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AttendanceRecord?)null);

        var result = await fixture.ClockOut.Handle(new ClockOutCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("not_clocked_in", result.Error);
        fixture.Attendance.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ClockOut_RejectsWhenClockInHasNotBeenSet()
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
                ActualStart = null
            });

        var result = await fixture.ClockOut.Handle(new ClockOutCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("not_clocked_in", result.Error);
    }

    [Fact]
    public async Task ClockOut_RejectsWhenDayIsAlreadyClosed()
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
                ActualStart = UtcNow.AddHours(-8),
                ActualEnd = UtcNow.AddHours(-1)
            });

        var result = await fixture.ClockOut.Handle(new ClockOutCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("already_clocked_out", result.Error);
    }

    [Fact]
    public async Task ClockOut_RejectsWhenBreakIsOpen()
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
                ActualStart = UtcNow.AddHours(-8),
                Status = AttendanceRecord.StatusActive
            });
        fixture.Attendance
            .Setup(x => x.HasOpenBreakAsync(
                TenantId, EmployeeId, LocalDayWindow.Start, LocalDayWindow.End, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await fixture.ClockOut.Handle(new ClockOutCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("open_break_must_be_ended_before_clock_out", result.Error);
        fixture.Attendance.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Fixture CreateFixture(
        AttendanceSchedule? schedule = null,
        AllowedClockInMethods? allowedMethods = null,
        string policyStatus = "configured",
        string expectedWorkArea = AttendanceRecord.WorkAreaRemote,
        string expectedWorkAreaSource = "active_employee_work_mode")
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
                Timezone = "Asia/Colombo"
            },
            "Asia/Colombo",
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo"),
            WorkDate,
            UtcNow,
            LocalNow,
            schedule ?? new AttendanceSchedule("configured", true, new(9, 0), new(17, 30), 510),
            expectedWorkArea,
            expectedWorkAreaSource,
            new ClockInPolicy { Id = Guid.NewGuid(), RemoteWebEnabled = true },
            policyStatus,
            allowedMethods ?? new AllowedClockInMethods(true, false, false, false, false, null),
            LocalDayWindow);

        var todayState = new Mock<IAttendanceTodayStateService>();
        todayState.Setup(x => x.ResolveContextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result<AttendanceTodayContext>.Success(context));
        todayState.Setup(x => x.GetTodayAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result<AttendanceTodayResponse>.Success(CreateTodayResponse()));

        var attendance = new Mock<IAttendanceReadRepository>();
        attendance.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result<bool>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<bool>>> operation, CancellationToken ct) => operation(ct));

        return new Fixture(
            new ClockInCommandHandler(todayState.Object, attendance.Object, unitOfWork.Object),
            new ClockOutCommandHandler(todayState.Object, attendance.Object, unitOfWork.Object),
            todayState,
            attendance,
            unitOfWork);
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
            AttendanceRecord.StatusClockedOut,
            null,
            null,
            0,
            null,
            false,
            false,
            false,
            false,
            false,
            false,
            new AllowedClockInMethods(true, false, false, false, false, null),
            []);

    private sealed record Fixture(
        ClockInCommandHandler ClockIn,
        ClockOutCommandHandler ClockOut,
        Mock<IAttendanceTodayStateService> TodayState,
        Mock<IAttendanceReadRepository> Attendance,
        Mock<IUnitOfWork> UnitOfWork);
}

