using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.ClockOut;
using ONEVO.Application.Features.TimeAttendance.Commands.EndBreak;
using ONEVO.Application.Features.TimeAttendance.Commands.StartBreak;
using ONEVO.Application.Features.TimeAttendance.Context;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class PresenceLifecycleCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 27, 17, 5, 0, TimeSpan.Zero);

    private readonly Mock<IClockInContextResolver> _contexts = new();
    private readonly Mock<ITimeAttendanceRepository> _attendance = new();
    private readonly Mock<IIdempotencyStore> _idempotency = new();
    private readonly Mock<IOutboxWriter> _outbox = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    [Fact]
    public async Task ClockOut_OpenBreak_ClosesAllPresenceStateAndSavesOnce()
    {
        var context = ConfigureContext();
        var device = new DeviceSession
        {
            Id = Guid.NewGuid(),
            TenantId = context.TenantId,
            EmployeeId = context.EmployeeId,
            DeviceId = context.AgentId,
            SessionStart = Now.AddHours(-8)
        };
        var attendance = new AttendanceRecord
        {
            Id = Guid.NewGuid(),
            TenantId = context.TenantId,
            EmployeeId = context.EmployeeId,
            Date = context.WorkDate,
            ActualStart = device.SessionStart,
            RequiredWorkMinutes = 480
        };
        var presence = new PresenceSession
        {
            Id = Guid.NewGuid(),
            TenantId = context.TenantId,
            EmployeeId = context.EmployeeId,
            Date = context.WorkDate,
            FirstSeenAt = device.SessionStart,
            LastSeenAt = device.SessionStart
        };
        var openBreak = new BreakRecord
        {
            Id = Guid.NewGuid(),
            TenantId = context.TenantId,
            EmployeeId = context.EmployeeId,
            BreakStart = Now.AddMinutes(-30),
            BreakType = "lunch"
        };
        ConfigurePresenceRecords(context, device, attendance, presence, openBreak);

        var result = await new ClockOutCommandHandler(
            _contexts.Object,
            _attendance.Object,
            _idempotency.Object,
            _outbox.Object,
            _clock.Object,
            _uow.Object).Handle(
                new ClockOutCommand(
                    context.AgentId,
                    $"clockout-{Guid.NewGuid():N}"),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("clocked_out", result.Value!.ClockOutStatus);
        Assert.Equal(Now, device.SessionEnd);
        Assert.Equal(Now, attendance.ActualEnd);
        Assert.Equal(Now, openBreak.BreakEnd);
        Assert.Equal(30, attendance.BreakMinutes);
        Assert.True(attendance.WorkedMinutes > 0);
        _outbox.Verify(writer => writer.EnqueueAsync(
            OutboxMessageTypes.PresenceSessionEnded,
            It.Is<PresenceSessionEndedEvent>(message =>
                message.AgentId == context.AgentId &&
                message.PresenceSessionId == presence.Id),
            context.TenantId,
            It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StartBreak_ActivePresence_CreatesBreakAndPausesMonitoring()
    {
        var context = ConfigureContext();
        var device = CreateOpenDevice(context);
        _attendance.Setup(repository => repository.GetOpenDeviceSessionAsync(
                context.AgentId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(device);
        _attendance.Setup(repository => repository.GetOpenBreakAsync(
                context.EmployeeId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BreakRecord?)null);

        var result = await new StartBreakCommandHandler(
            _contexts.Object,
            _attendance.Object,
            _outbox.Object,
            _clock.Object,
            _uow.Object).Handle(
                new StartBreakCommand(context.AgentId, "lunch"),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("break_started", result.Value!.Status);
        Assert.Equal("paused", result.Value.MonitoringState);
        _attendance.Verify(repository => repository.AddBreakAsync(
            It.Is<BreakRecord>(record =>
                record.EmployeeId == context.EmployeeId &&
                record.BreakStart == Now &&
                record.BreakType == "lunch"),
            It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EndBreak_OpenBreak_ClosesBreakAndResumesMonitoring()
    {
        var context = ConfigureContext();
        var device = CreateOpenDevice(context);
        var openBreak = new BreakRecord
        {
            Id = Guid.NewGuid(),
            TenantId = context.TenantId,
            EmployeeId = context.EmployeeId,
            BreakStart = Now.AddMinutes(-15),
            BreakType = "personal"
        };
        _attendance.Setup(repository => repository.GetOpenDeviceSessionAsync(
                context.AgentId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(device);
        _attendance.Setup(repository => repository.GetOpenBreakAsync(
                context.EmployeeId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(openBreak);

        var result = await new EndBreakCommandHandler(
            _contexts.Object,
            _attendance.Object,
            _outbox.Object,
            _clock.Object,
            _uow.Object).Handle(
                new EndBreakCommand(context.AgentId),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("break_ended", result.Value!.Status);
        Assert.Equal("running", result.Value.MonitoringState);
        Assert.Equal(Now, openBreak.BreakEnd);
        _uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private ResolvedClockInContext ConfigureContext()
    {
        var context = new ResolvedClockInContext(
            TenantId: Guid.NewGuid(),
            AgentId: Guid.NewGuid(),
            EmployeeId: Guid.NewGuid(),
            LegalEntityId: Guid.NewGuid(),
            WorkDate: new DateOnly(2026, 7, 27),
            Timezone: "UTC",
            WorkScheduleId: Guid.NewGuid(),
            WorkScheduleName: "Weekday",
            ScheduledStart: new TimeOnly(9, 0),
            ScheduledEnd: new TimeOnly(17, 0),
            RequiredWorkMinutes: 480,
            ExpectedWorkArea: "onsite",
            WorkAreaSource: "work_schedule",
            ClockInPolicyId: Guid.NewGuid(),
            LocationRequired: false,
            LocationTargets: [],
            PhotoRequired: false,
            ReferenceReady: true,
            RemoteProfileReady: false,
            IsHoliday: false,
            IsWorkingDay: true,
            IsClockedIn: true,
            CanClockIn: false,
            ReasonCode: "already_clocked_in",
            MonitoringHardStopAt: Now.AddMinutes(-5));

        _clock.SetupGet(provider => provider.UtcNow).Returns(Now);
        _contexts.Setup(resolver => resolver.ResolveAsync(
                context.AgentId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ResolvedClockInContext>.Success(context));
        _idempotency.Setup(store => store.TryBeginAsync(
                It.IsAny<string>(),
                "time_attendance.clock_out",
                $"agent:{context.AgentId}",
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyBeginResult(
                IdempotencyOutcome.Started,
                Guid.NewGuid()));
        _uow.Setup(unit => unit.SaveChangesAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        return context;
    }

    private void ConfigurePresenceRecords(
        ResolvedClockInContext context,
        DeviceSession device,
        AttendanceRecord attendance,
        PresenceSession presence,
        BreakRecord? openBreak)
    {
        _attendance.Setup(repository => repository.GetOpenDeviceSessionAsync(
                context.AgentId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(device);
        _attendance.Setup(repository => repository.GetAttendanceAsync(
                context.EmployeeId,
                context.WorkDate,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(attendance);
        _attendance.Setup(repository => repository.GetPresenceAsync(
                context.EmployeeId,
                context.WorkDate,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(presence);
        _attendance.Setup(repository => repository.GetOpenBreakAsync(
                context.EmployeeId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(openBreak);
    }

    private static DeviceSession CreateOpenDevice(
        ResolvedClockInContext context) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = context.TenantId,
        EmployeeId = context.EmployeeId,
        DeviceId = context.AgentId,
        SessionStart = Now.AddHours(-2)
    };
}
