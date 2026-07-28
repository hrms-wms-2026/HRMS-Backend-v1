using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.DTOs;
using ONEVO.Application.Features.AgentGateway.Policy;
using ONEVO.Application.Features.AgentGateway.Queries.GetAgentWorkContext;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Context;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public sealed class GetAgentWorkContextQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 27, 10, 30, 0, TimeSpan.Zero);

    private readonly Mock<IAgentGatewayRepository> _agents = new();
    private readonly Mock<ITimeAttendanceRepository> _attendance = new();
    private readonly Mock<IClockInContextResolver> _contexts = new();
    private readonly Mock<IEffectiveAgentPolicyResolver> _policyResolver = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public GetAgentWorkContextQueryHandlerTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _policyResolver
            .Setup(r => r.Resolve(It.IsAny<string?>(), It.IsAny<EffectiveAgentPolicyContext>()))
            .Returns(DisabledPolicy());
    }

    // ── Active monitoring ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ActiveSession_NoBreak_ReturnsActiveMonitoringState()
    {
        var agent = ActiveAgent();
        var context = WorkingDayContext(agent, hardStopAt: Now.AddHours(7));
        var session = OpenSession(agent, context);

        SetupAgent(agent, context, session, openBreak: null);

        var result = await CreateHandler().Handle(
            new GetAgentWorkContextQuery(agent.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Active", result.Value!.MonitoringState);
        Assert.Equal(session.Id, result.Value.PresenceSessionId);
        Assert.Null(result.Value.BreakId);
    }

    // ── Break (Paused) ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ActiveSession_WithOpenBreak_ReturnsPausedMonitoringState()
    {
        var agent = ActiveAgent();
        var context = WorkingDayContext(agent, hardStopAt: Now.AddHours(7));
        var session = OpenSession(agent, context);
        var openBreak = new BreakRecord
        {
            Id = Guid.NewGuid(),
            TenantId = agent.TenantId,
            EmployeeId = agent.EmployeeId!.Value,
            BreakStart = Now.AddMinutes(-15),
            BreakType = "lunch"
        };

        SetupAgent(agent, context, session, openBreak);

        var result = await CreateHandler().Handle(
            new GetAgentWorkContextQuery(agent.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Paused", result.Value!.MonitoringState);
        Assert.Equal(session.Id, result.Value.PresenceSessionId);
        Assert.Equal(openBreak.Id, result.Value.BreakId);
    }

    // ── Clocked Out (Stopped) ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NoOpenDeviceSession_ReturnsStoppedMonitoringState()
    {
        var agent = ActiveAgent();
        var context = WorkingDayContext(agent, hardStopAt: Now.AddHours(7));

        SetupAgent(agent, context, session: null, openBreak: null);

        var result = await CreateHandler().Handle(
            new GetAgentWorkContextQuery(agent.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Stopped", result.Value!.MonitoringState);
        Assert.Null(result.Value.PresenceSessionId);
    }

    // ── Revoked Device (Stopped) ──────────────────────────────────────────────

    [Fact]
    public async Task Handle_RevokedAgent_ReturnsStoppedAndNoSchedule()
    {
        var agent = ActiveAgent();
        agent.Status = "revoked";

        _agents.Setup(r => r.GetAgentByIdAsync(agent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        var result = await CreateHandler().Handle(
            new GetAgentWorkContextQuery(agent.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Stopped", result.Value!.MonitoringState);
        Assert.Null(result.Value.ScheduleName);
    }

    [Fact]
    public async Task Handle_AgentNotFound_ReturnsStoppedAndNoSchedule()
    {
        _agents.Setup(r => r.GetAgentByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegisteredAgent?)null);

        var result = await CreateHandler().Handle(
            new GetAgentWorkContextQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Stopped", result.Value!.MonitoringState);
    }

    // ── Holiday (Stopped) ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_HolidayContext_ReturnsStoppedWithHolidayDayType()
    {
        var agent = ActiveAgent();
        var context = WorkingDayContext(agent, hardStopAt: Now.AddHours(7)) with
        {
            IsHoliday = true,
            IsWorkingDay = false,
            CanClockIn = false,
            ReasonCode = "holiday"
        };

        SetupAgent(agent, context, session: null, openBreak: null);

        var result = await CreateHandler().Handle(
            new GetAgentWorkContextQuery(agent.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Stopped", result.Value!.MonitoringState);
        Assert.Equal("holiday", result.Value.DayType);
    }

    // ── Time Off (Stopped) ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_TimeOffContext_ReturnsStoppedWithTimeOffDayType()
    {
        var agent = ActiveAgent();
        var context = WorkingDayContext(agent, hardStopAt: Now.AddHours(7)) with
        {
            IsHoliday = false,
            IsWorkingDay = false,
            CanClockIn = false,
            ReasonCode = "time_off"
        };

        SetupAgent(agent, context, session: null, openBreak: null);

        var result = await CreateHandler().Handle(
            new GetAgentWorkContextQuery(agent.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Stopped", result.Value!.MonitoringState);
        Assert.Equal("time_off", result.Value.DayType);
    }

    // ── Hard stop in the past (Stopped) ──────────────────────────────────────

    [Fact]
    public async Task Handle_HardStopExpired_ReturnsStoppedEvenWithOpenSession()
    {
        var agent = ActiveAgent();
        var context = WorkingDayContext(agent, hardStopAt: Now.AddMinutes(-5));
        var session = OpenSession(agent, context);

        SetupAgent(agent, context, session, openBreak: null);

        var result = await CreateHandler().Handle(
            new GetAgentWorkContextQuery(agent.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Stopped", result.Value!.MonitoringState);
    }

    // ── Response shape ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ActiveSession_IncludesScheduleAndPolicyInResponse()
    {
        var agent = ActiveAgent();
        var context = WorkingDayContext(agent, hardStopAt: Now.AddHours(7));
        var session = OpenSession(agent, context);

        SetupAgent(agent, context, session, openBreak: null);

        var result = await CreateHandler().Handle(
            new GetAgentWorkContextQuery(agent.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = result.Value!;
        Assert.Equal(Now, dto.ServerTime);
        Assert.Equal(context.WorkScheduleName, dto.ScheduleName);
        Assert.Equal(context.Timezone, dto.Timezone);
        Assert.Equal(context.ScheduledStart, dto.ScheduledStart);
        Assert.Equal(context.ScheduledEnd, dto.ScheduledEnd);
        Assert.Equal(context.RequiredWorkMinutes, dto.RequiredWorkMinutes);
        Assert.Equal(context.ExpectedWorkArea, dto.ExpectedWorkArea);
        Assert.Equal(context.MonitoringHardStopAt, dto.HardStopAt);
        Assert.NotNull(dto.EffectivePolicy);
        Assert.Empty(dto.AssignedTasks);
        Assert.Empty(dto.ActiveTaskTimers);
    }

    // ── Fail-closed: context resolver failure ─────────────────────────────────

    [Fact]
    public async Task Handle_ContextResolutionFails_ReturnsStoppedNotError()
    {
        var agent = ActiveAgent();
        _agents.Setup(r => r.GetAgentByIdAsync(agent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        _contexts.Setup(r => r.ResolveAsync(agent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ResolvedClockInContext>.Failure("No schedule found.", 404));

        var result = await CreateHandler().Handle(
            new GetAgentWorkContextQuery(agent.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Stopped", result.Value!.MonitoringState);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private GetAgentWorkContextQueryHandler CreateHandler() =>
        new(_agents.Object, _attendance.Object, _contexts.Object,
            _policyResolver.Object, _clock.Object);

    private void SetupAgent(
        RegisteredAgent agent,
        ResolvedClockInContext? context,
        DeviceSession? session,
        BreakRecord? openBreak)
    {
        _agents.Setup(r => r.GetAgentByIdAsync(agent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        _agents.Setup(r => r.GetPolicyByAgentIdAsync(agent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentPolicy?)null);

        if (context is not null)
        {
            _contexts.Setup(r => r.ResolveAsync(agent.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<ResolvedClockInContext>.Success(context));
        }

        _attendance.Setup(r => r.GetOpenDeviceSessionAsync(agent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        if (session is not null && agent.EmployeeId.HasValue)
        {
            _attendance.Setup(r => r.GetOpenBreakAsync(agent.EmployeeId.Value, It.IsAny<CancellationToken>()))
                .ReturnsAsync(openBreak);
        }
    }

    private static RegisteredAgent ActiveAgent() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        DeviceId = "approved-device",
        Status = "active"
    };

    private static ResolvedClockInContext WorkingDayContext(
        RegisteredAgent agent,
        DateTimeOffset hardStopAt) => new(
        TenantId: agent.TenantId,
        AgentId: agent.Id,
        EmployeeId: agent.EmployeeId!.Value,
        LegalEntityId: Guid.NewGuid(),
        WorkDate: DateOnly.FromDateTime(Now.UtcDateTime),
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
        MonitoringHardStopAt: hardStopAt);

    private static DeviceSession OpenSession(RegisteredAgent agent, ResolvedClockInContext context) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = agent.TenantId,
            EmployeeId = agent.EmployeeId!.Value,
            DeviceId = agent.Id,
            SessionStart = Now.AddHours(-2)
        };

    private static EffectiveAgentPolicy DisabledPolicy() => new(
        Version: 1,
        ActivityMonitoring: false,
        ApplicationTracking: false,
        MeetingDetection: false,
        ScreenshotCapture: false,
        SnapshotIntervalSeconds: 60,
        AppSampleIntervalSeconds: 5,
        IdleThresholdSeconds: 900,
        ScreenshotConsentTimeoutSeconds: 30,
        ScreenshotCooldownSeconds: 900,
        ScreenshotScope: "active_monitor",
        MaxScreenshotBytes: 2097152);
}
