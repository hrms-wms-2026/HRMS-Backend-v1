using System.Net;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.Location;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.ClockIn;
using ONEVO.Application.Features.TimeAttendance.Context;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class ClockInCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 27, 8, 55, 0, TimeSpan.Zero);

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

    [Fact]
    public async Task Handle_MatchingLocation_CreatesAttendancePresenceDeviceAndOutbox()
    {
        var context = ConfigureContext();
        var command = CreateCommand(context.AgentId, 13.0827m, 80.2707m);

        var result = await CreateHandler().Handle(
            command,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("clocked_in", result.Value!.ClockInStatus);
        Assert.Equal(200, result.Value.HttpStatusCode);
        _attendance.Verify(repository => repository.AddAttendanceAsync(
            It.Is<AttendanceRecord>(record =>
                record.TenantId == context.TenantId &&
                record.EmployeeId == context.EmployeeId &&
                record.ActualStart == Now),
            It.IsAny<CancellationToken>()), Times.Once);
        _attendance.Verify(repository => repository.AddPresenceAsync(
            It.Is<PresenceSession>(session =>
                session.EmployeeId == context.EmployeeId &&
                session.FirstSeenAt == Now),
            It.IsAny<CancellationToken>()), Times.Once);
        _attendance.Verify(repository => repository.AddDeviceSessionAsync(
            It.Is<DeviceSession>(session =>
                session.DeviceId == context.AgentId &&
                session.SessionStart == Now),
            It.IsAny<CancellationToken>()), Times.Once);
        _agents.Verify(repository => repository.AddWorkLocationEvidenceAsync(
            It.Is<AgentWorkLocationEvidence>(evidence =>
                evidence.MatchStatus == "matched" &&
                evidence.PresenceSessionId == result.Value.PresenceSessionId),
            It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(writer => writer.EnqueueAsync(
            OutboxMessageTypes.PresenceSessionStarted,
            It.Is<PresenceSessionStartedEvent>(message =>
                message.AgentId == context.AgentId &&
                message.EmployeeId == context.EmployeeId),
            context.TenantId,
            It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_LocationMismatch_CreatesPendingHrApprovalWithoutAttendance()
    {
        var context = ConfigureContext();
        var command = CreateCommand(context.AgentId, 6.9271m, 79.8612m);

        var result = await CreateHandler().Handle(
            command,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("blocked_pending_approval", result.Value!.ClockInStatus);
        Assert.Equal(409, result.Value.HttpStatusCode);
        Assert.NotNull(result.Value.ApprovalRequestId);
        Assert.Equal("work_area_change", result.Value.ApprovalRequestType);
        _attendance.Verify(repository => repository.AddWorkAreaChangeAsync(
            It.Is<WorkAreaChangeRequest>(request =>
                request.EmployeeId == context.EmployeeId &&
                request.Status == "pending" &&
                request.CurrentExpectedWorkArea == "onsite" &&
                request.RequestedWorkArea == "remote"),
            It.IsAny<CancellationToken>()), Times.Once);
        _attendance.Verify(repository => repository.AddAttendanceAsync(
            It.IsAny<AttendanceRecord>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _attendance.Verify(repository => repository.AddPresenceAsync(
            It.IsAny<PresenceSession>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _outbox.Verify(writer => writer.EnqueueAsync(
            It.IsAny<string>(),
            It.IsAny<object>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private ClockInCommandHandler CreateHandler() => new(
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

    private ResolvedClockInContext ConfigureContext()
    {
        var tenantId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var context = new ResolvedClockInContext(
            TenantId: tenantId,
            AgentId: agentId,
            EmployeeId: employeeId,
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
            LocationRequired: true,
            LocationTargets:
            [
                new LocationTarget(
                    Guid.NewGuid(),
                    "company_office",
                    13.0827m,
                    80.2707m,
                    250)
            ],
            PhotoRequired: false,
            ReferenceReady: true,
            RemoteProfileReady: false,
            IsHoliday: false,
            IsWorkingDay: true,
            IsClockedIn: false,
            CanClockIn: true,
            ReasonCode: "ready",
            MonitoringHardStopAt: new DateTimeOffset(
                2026,
                7,
                27,
                17,
                0,
                0,
                TimeSpan.Zero));

        _clock.SetupGet(provider => provider.UtcNow).Returns(Now);
        _contexts.Setup(resolver => resolver.ResolveAsync(
                agentId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ONEVO.Application.Common.Models.Result<ResolvedClockInContext>
                .Success(context));
        _network.SetupGet(network => network.ClientIp)
            .Returns(IPAddress.Parse("203.0.113.10"));
        _hasher.Setup(hasher => hasher.Protect(
                tenantId,
                It.IsAny<string?>()))
            .Returns((Guid _, string? value) => value);
        _idempotency.Setup(store => store.TryBeginAsync(
                It.IsAny<string>(),
                "time_attendance.clock_in",
                $"agent:{agentId}",
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyBeginResult(
                IdempotencyOutcome.Started,
                Guid.NewGuid()));
        _attendance.Setup(repository => repository.GetAttendanceAsync(
                employeeId,
                context.WorkDate,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AttendanceRecord?)null);
        _attendance.Setup(repository => repository.GetPresenceAsync(
                employeeId,
                context.WorkDate,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PresenceSession?)null);
        _attendance.Setup(repository => repository.GetPendingWorkAreaChangeAsync(
                employeeId,
                context.WorkDate,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkAreaChangeRequest?)null);
        _uow.Setup(unit => unit.SaveChangesAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        return context;
    }

    private static ClockInCommand CreateCommand(
        Guid agentId,
        decimal latitude,
        decimal longitude) => new(
        AgentId: agentId,
        IdempotencyKey: $"clockin-{Guid.NewGuid():N}",
        Capture: new LocationCapture(
            latitude,
            longitude,
            20,
            Now.AddSeconds(-5),
            "granted"),
        LocalNetworkClass: "private",
        WifiBssidHash: null,
        GatewayMacHash: null,
        VpnDetected: false,
        VerificationRecordId: null);
}
