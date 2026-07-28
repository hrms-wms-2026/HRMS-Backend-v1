using System.Net;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.Location;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.ClockIn;
using ONEVO.Application.Features.TimeAttendance.Commands.ClockOut;
using ONEVO.Application.Features.TimeAttendance.Commands.StartBreak;
using ONEVO.Application.Features.TimeAttendance.Context;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class AttendanceLifecycleTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 27, 17, 5, 0, TimeSpan.Zero);

    private readonly Mock<IClockInContextResolver> _contexts = new();
    private readonly Mock<IAgentGatewayRepository> _agents = new();
    private readonly Mock<ITimeAttendanceRepository> _attendance = new();
    private readonly Mock<IVerificationRepository> _verification = new();
    private readonly Mock<IRequestNetworkContext> _network = new();
    private readonly Mock<INetworkEvidenceHasher> _hasher = new();
    private readonly Mock<IIdempotencyStore> _idempotency = new();
    private readonly Mock<IOutboxWriter> _outbox = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    // ── Duplicate Clock In ────────────────────────────────────────────────────

    [Fact]
    public async Task ClockIn_WhenAlreadyClockedIn_ReturnsAlreadyClockedInStatus()
    {
        var context = BuildContext(isClockedIn: true, canClockIn: false, reasonCode: "already_clocked_in");

        var result = await CreateClockInHandler().Handle(
            CreateClockInCommand(context.AgentId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("already_clocked_in", result.Value!.ClockInStatus);
        Assert.Equal(200, result.Value.HttpStatusCode);
        _attendance.Verify(r => r.AddAttendanceAsync(
            It.IsAny<AttendanceRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ClockIn_WhenSetupBlockedAndNotClockedIn_ReturnsBlockedSetupRequired()
    {
        var context = BuildContext(isClockedIn: false, canClockIn: false, reasonCode: "approval_required");

        var result = await CreateClockInHandler().Handle(
            CreateClockInCommand(context.AgentId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("blocked_setup_required", result.Value!.ClockInStatus);
        Assert.Equal(409, result.Value.HttpStatusCode);
        _attendance.Verify(r => r.AddAttendanceAsync(
            It.IsAny<AttendanceRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Break lifecycle — pause committed with break record ───────────────────

    [Fact]
    public async Task StartBreak_ActiveDevice_EnqueuesBreakEventInSameCommit()
    {
        var context = BuildContext();
        _attendance.Setup(r => r.GetOpenDeviceSessionAsync(context.AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceSession
            {
                Id = Guid.NewGuid(),
                TenantId = context.TenantId,
                EmployeeId = context.EmployeeId,
                DeviceId = context.AgentId,
                SessionStart = Now.AddHours(-2)
            });
        _attendance.Setup(r => r.GetOpenBreakAsync(context.EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BreakRecord?)null);

        var result = await new StartBreakCommandHandler(
                _contexts.Object, _attendance.Object, _outbox.Object, _clock.Object, _uow.Object)
            .Handle(new StartBreakCommand(context.AgentId, "lunch"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("paused", result.Value!.MonitoringState);

        _outbox.Verify(w => w.EnqueueAsync(
            OutboxMessageTypes.PresenceBreakStarted,
            It.Is<PresenceBreakStartedEvent>(e =>
                e.AgentId == context.AgentId &&
                e.EmployeeId == context.EmployeeId),
            context.TenantId,
            It.IsAny<CancellationToken>()), Times.Once);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartBreak_NoActiveDeviceSession_ReturnsConflict()
    {
        var context = BuildContext();
        _attendance.Setup(r => r.GetOpenDeviceSessionAsync(context.AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeviceSession?)null);

        var result = await new StartBreakCommandHandler(
                _contexts.Object, _attendance.Object, _outbox.Object, _clock.Object, _uow.Object)
            .Handle(new StartBreakCommand(context.AgentId, "lunch"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _outbox.Verify(w => w.EnqueueAsync(
            It.IsAny<string>(), It.IsAny<object>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartBreak_AlreadyOnBreak_ReturnsIdempotentBreakAlreadyStarted()
    {
        var context = BuildContext();
        var existingBreak = new BreakRecord
        {
            Id = Guid.NewGuid(),
            TenantId = context.TenantId,
            EmployeeId = context.EmployeeId,
            BreakStart = Now.AddMinutes(-10),
            BreakType = "personal"
        };
        _attendance.Setup(r => r.GetOpenDeviceSessionAsync(context.AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceSession
            {
                Id = Guid.NewGuid(),
                TenantId = context.TenantId,
                EmployeeId = context.EmployeeId,
                DeviceId = context.AgentId,
                SessionStart = Now.AddHours(-2)
            });
        _attendance.Setup(r => r.GetOpenBreakAsync(context.EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBreak);

        var result = await new StartBreakCommandHandler(
                _contexts.Object, _attendance.Object, _outbox.Object, _clock.Object, _uow.Object)
            .Handle(new StartBreakCommand(context.AgentId, "lunch"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("break_already_started", result.Value!.Status);
        Assert.Equal("paused", result.Value.MonitoringState);
        _attendance.Verify(r => r.AddBreakAsync(It.IsAny<BreakRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Clock Out — sessions close before stop_monitoring outbox event ────────

    [Fact]
    public async Task ClockOut_ActiveSession_ClosesDeviceSessionBeforeOutboxEvent()
    {
        var context = BuildContext();
        var deviceSession = new DeviceSession
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
            ActualStart = deviceSession.SessionStart,
            RequiredWorkMinutes = 480
        };
        var presence = new PresenceSession
        {
            Id = Guid.NewGuid(),
            TenantId = context.TenantId,
            EmployeeId = context.EmployeeId,
            Date = context.WorkDate,
            FirstSeenAt = deviceSession.SessionStart,
            LastSeenAt = deviceSession.SessionStart
        };
        _attendance.Setup(r => r.GetOpenDeviceSessionAsync(context.AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deviceSession);
        _attendance.Setup(r => r.GetAttendanceAsync(context.EmployeeId, context.WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attendance);
        _attendance.Setup(r => r.GetPresenceAsync(context.EmployeeId, context.WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(presence);
        _attendance.Setup(r => r.GetOpenBreakAsync(context.EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BreakRecord?)null);

        var sessionClosedAtOutboxTime = false;
        _outbox.Setup(w => w.EnqueueAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, Guid?, CancellationToken>((_, _, _, _) =>
            {
                sessionClosedAtOutboxTime = deviceSession.SessionEnd is not null;
            })
            .Returns(Task.CompletedTask);

        var result = await new ClockOutCommandHandler(
                _contexts.Object, _attendance.Object, _idempotency.Object,
                _outbox.Object, _clock.Object, _uow.Object)
            .Handle(
                new ClockOutCommand(context.AgentId, $"clockout-{Guid.NewGuid():N}"),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("clocked_out", result.Value!.ClockOutStatus);
        Assert.True(sessionClosedAtOutboxTime,
            "device session must be closed before the outbox event is enqueued");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private ResolvedClockInContext BuildContext(
        bool isClockedIn = true,
        bool canClockIn = false,
        string reasonCode = "already_clocked_in")
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
            IsClockedIn: isClockedIn,
            CanClockIn: canClockIn,
            ReasonCode: reasonCode,
            MonitoringHardStopAt: Now.AddMinutes(-5));

        _clock.SetupGet(p => p.UtcNow).Returns(Now);
        _contexts.Setup(r => r.ResolveAsync(context.AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ResolvedClockInContext>.Success(context));
        _idempotency.Setup(s => s.TryBeginAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyBeginResult(IdempotencyOutcome.Started, Guid.NewGuid()));
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _outbox.Setup(w => w.EnqueueAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _network.SetupGet(n => n.ClientIp).Returns(IPAddress.Parse("203.0.113.10"));
        _hasher.Setup(h => h.Protect(It.IsAny<Guid>(), It.IsAny<string?>()))
            .Returns((Guid _, string? v) => v);

        return context;
    }

    private ClockInCommandHandler CreateClockInHandler() => new(
        _contexts.Object,
        _agents.Object,
        _attendance.Object,
        _verification.Object,
        new LocationVerificationService(),
        _network.Object,
        _hasher.Object,
        _idempotency.Object,
        _outbox.Object,
        _clock.Object,
        _uow.Object);

    private static ClockInCommand CreateClockInCommand(Guid agentId) => new(
        AgentId: agentId,
        IdempotencyKey: $"clockin-{Guid.NewGuid():N}",
        Capture: null,
        LocalNetworkClass: "private",
        WifiBssidHash: null,
        GatewayMacHash: null,
        VpnDetected: false,
        VerificationRecordId: null);
}
