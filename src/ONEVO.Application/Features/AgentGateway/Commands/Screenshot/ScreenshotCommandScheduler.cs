using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.Policy;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.Users.RepositoryInterfaces;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Application.Features.AgentGateway.Commands.Screenshot;

public sealed class ScreenshotCommandScheduler
    : IScreenshotCommandScheduler
{
    private readonly IAgentGatewayRepository _agents;
    private readonly ITimeAttendanceRepository _attendance;
    private readonly IUserProfileRepository _profiles;
    private readonly IVerificationRepository _verification;
    private readonly IEffectiveAgentPolicyResolver _policyResolver;
    private readonly IDateTimeProvider _clock;

    public ScreenshotCommandScheduler(
        IAgentGatewayRepository agents,
        ITimeAttendanceRepository attendance,
        IUserProfileRepository profiles,
        IVerificationRepository verification,
        IEffectiveAgentPolicyResolver policyResolver,
        IDateTimeProvider clock)
    {
        _agents = agents;
        _attendance = attendance;
        _profiles = profiles;
        _verification = verification;
        _policyResolver = policyResolver;
        _clock = clock;
    }

    public async Task<bool> TryScheduleAsync(
        RegisteredAgent agent,
        ActivitySnapshot snapshot,
        CancellationToken ct)
    {
        if (snapshot.KeyboardEventsCount != 0 ||
            snapshot.MouseEventsCount != 0 ||
            agent.EmployeeId is null ||
            agent.EmployeeId.Value != snapshot.EmployeeId ||
            agent.TenantId != snapshot.TenantId ||
            !string.Equals(agent.Status, "active", StringComparison.Ordinal))
        {
            return false;
        }

        var policy = await _agents.GetPolicyByAgentIdAsync(agent.Id, ct);
        if (policy is null ||
            policy.TenantId != agent.TenantId ||
            policy.AgentId != agent.Id)
        {
            return false;
        }

        var session = await _agents.GetActiveSessionByDeviceIdAsync(
            agent.DeviceId,
            ct);
        var activeAgentSession =
            session is not null &&
            session.IsActive &&
            session.TenantId == agent.TenantId &&
            session.EmployeeId == agent.EmployeeId.Value &&
            string.Equals(session.DeviceId, agent.DeviceId, StringComparison.Ordinal);

        var deviceSession = await _attendance.GetOpenDeviceSessionAsync(
            agent.Id,
            ct);
        var activePresence =
            deviceSession is not null &&
            deviceSession.SessionEnd is null &&
            deviceSession.TenantId == agent.TenantId &&
            deviceSession.EmployeeId == agent.EmployeeId.Value &&
            deviceSession.DeviceId == agent.Id;

        var employee = await _profiles.GetEmployeeByIdAsync(
            agent.EmployeeId.Value,
            ct);
        var disclosureAccepted = false;
        if (employee is not null && employee.TenantId == agent.TenantId)
        {
            var consent = await _verification.GetLatestConsentAsync(
                agent.TenantId,
                employee.UserId,
                "monitoring",
                ct);
            disclosureAccepted =
                consent is not null &&
                consent.TenantId == agent.TenantId &&
                consent.UserId == employee.UserId &&
                consent.Consented;
        }

        var effectivePolicy = _policyResolver.Resolve(
            policy.PolicyJson,
            new EffectiveAgentPolicyContext(
                DeviceApproved: true,
                ActiveAgentSession: activeAgentSession,
                ActivePresence: activePresence,
                MonitoringDisclosureAccepted: disclosureAccepted));
        if (!effectivePolicy.ScreenshotCapture ||
            snapshot.IdleSeconds < effectivePolicy.IdleThresholdSeconds)
        {
            return false;
        }

        var now = _clock.UtcNow;
        var latest = await _agents.GetLatestCommandAsync(
            agent.Id,
            "screenshot_capture_request",
            ct);
        if (latest is not null &&
            (latest.Status is "pending" or "accepted" ||
             latest.CreatedAt.AddSeconds(
                 effectivePolicy.ScreenshotCooldownSeconds) > now))
        {
            return false;
        }

        var expiresAt = now.AddSeconds(
            effectivePolicy.ScreenshotConsentTimeoutSeconds);
        await _agents.AddCommandAsync(new AgentCommand
        {
            Id = Guid.NewGuid(),
            TenantId = agent.TenantId,
            AgentId = agent.Id,
            EmployeeId = agent.EmployeeId.Value,
            ActivitySnapshotId = snapshot.Id,
            CommandType = "screenshot_capture_request",
            PayloadJson = JsonSerializer.Serialize(new
            {
                reason_code = "idle_threshold_reached",
                idle_seconds = snapshot.IdleSeconds,
                consent_notice_version = "monitoring-screenshot-v1",
                screenshot_scope = effectivePolicy.ScreenshotScope,
                max_screenshot_bytes = effectivePolicy.MaxScreenshotBytes,
                requested_at = now,
                expires_at = expiresAt
            }),
            Status = "pending",
            CreatedAt = now,
            AvailableAt = now,
            ExpiresAt = expiresAt
        }, ct);
        return true;
    }
}

