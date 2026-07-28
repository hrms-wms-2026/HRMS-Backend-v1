using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.DTOs;
using ONEVO.Application.Features.AgentGateway.Policy;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Context;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.AgentGateway.Queries.GetAgentWorkContext;

public sealed class GetAgentWorkContextQueryHandler
    : IRequestHandler<GetAgentWorkContextQuery, Result<AgentWorkContextDto>>
{
    private readonly IAgentGatewayRepository _agents;
    private readonly ITimeAttendanceRepository _attendance;
    private readonly IClockInContextResolver _contexts;
    private readonly IEffectiveAgentPolicyResolver _policyResolver;
    private readonly IDateTimeProvider _clock;

    public GetAgentWorkContextQueryHandler(
        IAgentGatewayRepository agents,
        ITimeAttendanceRepository attendance,
        IClockInContextResolver contexts,
        IEffectiveAgentPolicyResolver policyResolver,
        IDateTimeProvider clock)
    {
        _agents = agents;
        _attendance = attendance;
        _contexts = contexts;
        _policyResolver = policyResolver;
        _clock = clock;
    }

    public async Task<Result<AgentWorkContextDto>> Handle(
        GetAgentWorkContextQuery request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var agent = await _agents.GetAgentByIdAsync(request.AgentId, cancellationToken);
        if (agent is null || !string.Equals(agent.Status, "active", StringComparison.Ordinal))
            return Result<AgentWorkContextDto>.Success(StoppedContext(now));

        var contextResult = await _contexts.ResolveAsync(request.AgentId, cancellationToken);
        if (!contextResult.IsSuccess)
            return Result<AgentWorkContextDto>.Success(StoppedContext(now));

        var context = contextResult.Value!;
        var dayType = ResolveDayType(context);
        var policy = await _agents.GetPolicyByAgentIdAsync(request.AgentId, cancellationToken);
        var effectivePolicy = _policyResolver.Resolve(
            policy?.PolicyJson,
            new EffectiveAgentPolicyContext(
                DeviceApproved: true,
                ActiveAgentSession: false,
                ActivePresence: false,
                MonitoringDisclosureAccepted: false));

        var session = await _attendance.GetOpenDeviceSessionAsync(request.AgentId, cancellationToken);

        var monitoringState = "Stopped";
        Guid? presenceSessionId = null;
        Guid? breakId = null;

        if (session is not null &&
            session.TenantId == agent.TenantId &&
            session.EmployeeId == agent.EmployeeId &&
            session.DeviceId == agent.Id &&
            session.SessionEnd is null)
        {
            var hardStop = context.MonitoringHardStopAt;
            if (hardStop.HasValue && now >= hardStop.Value)
            {
                monitoringState = "Stopped";
            }
            else
            {
                presenceSessionId = session.Id;

                var openBreak = agent.EmployeeId.HasValue
                    ? await _attendance.GetOpenBreakAsync(agent.EmployeeId.Value, cancellationToken)
                    : null;

                if (openBreak is not null &&
                    openBreak.TenantId == agent.TenantId &&
                    openBreak.BreakEnd is null)
                {
                    monitoringState = "Paused";
                    breakId = openBreak.Id;
                }
                else
                {
                    monitoringState = "Active";
                }
            }
        }

        return Result<AgentWorkContextDto>.Success(new AgentWorkContextDto(
            ServerTime: now,
            MonitoringState: monitoringState,
            PresenceSessionId: presenceSessionId,
            BreakId: breakId,
            ScheduleName: context.WorkScheduleName,
            Timezone: context.Timezone,
            DayType: dayType,
            ScheduledStart: context.ScheduledStart,
            ScheduledEnd: context.ScheduledEnd,
            RequiredWorkMinutes: context.RequiredWorkMinutes,
            ExpectedWorkArea: context.ExpectedWorkArea,
            HardStopAt: context.MonitoringHardStopAt,
            EffectivePolicy: effectivePolicy,
            TaskFeatureAvailable: false,
            AssignedTasks: [],
            ActiveTaskTimers: []));
    }

    private static string ResolveDayType(ResolvedClockInContext context)
    {
        if (context.IsHoliday) return "holiday";
        if (!context.IsWorkingDay &&
            string.Equals(context.ReasonCode, "time_off", StringComparison.Ordinal))
            return "time_off";
        if (!context.IsWorkingDay) return "non_work_day";
        return "work_day";
    }

    private static AgentWorkContextDto StoppedContext(DateTimeOffset now) => new(
        ServerTime: now,
        MonitoringState: "Stopped",
        PresenceSessionId: null,
        BreakId: null,
        ScheduleName: null,
        Timezone: null,
        DayType: null,
        ScheduledStart: null,
        ScheduledEnd: null,
        RequiredWorkMinutes: null,
        ExpectedWorkArea: null,
        HardStopAt: null,
        EffectivePolicy: DisabledPolicy(),
        TaskFeatureAvailable: false,
        AssignedTasks: [],
        ActiveTaskTimers: []);

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
