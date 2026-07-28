using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Application.Features.AgentGateway.Commands.RespondAgentCommand;

public sealed class RespondAgentCommandHandler
    : IRequestHandler<
        RespondAgentCommand,
        Result<AgentCommandDecisionResponse>>
{
    private static readonly TimeSpan CaptureWindow = TimeSpan.FromMinutes(2);

    private readonly IAgentGatewayRepository _agents;
    private readonly ITimeAttendanceRepository _attendance;
    private readonly IActivityMonitoringRepository _monitoring;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public RespondAgentCommandHandler(
        IAgentGatewayRepository agents,
        ITimeAttendanceRepository attendance,
        IActivityMonitoringRepository monitoring,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _agents = agents;
        _attendance = attendance;
        _monitoring = monitoring;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result<AgentCommandDecisionResponse>> Handle(
        RespondAgentCommand request,
        CancellationToken cancellationToken)
    {
        var decision = request.Decision.Trim().ToLowerInvariant();
        if (decision is not ("allow" or "deny"))
        {
            return Result<AgentCommandDecisionResponse>.Failure(
                "Decision must be allow or deny.",
                400);
        }
        if (string.IsNullOrWhiteSpace(request.ConsentNoticeVersion) ||
            request.ConsentNoticeVersion.Length > 50)
        {
            return Result<AgentCommandDecisionResponse>.Failure(
                "A valid consent notice version is required.",
                400);
        }

        var agent = await _agents.GetAgentByIdAsync(
            request.AgentId,
            cancellationToken);
        if (agent is null ||
            agent.EmployeeId is null ||
            !string.Equals(agent.Status, "active", StringComparison.Ordinal))
        {
            return Result<AgentCommandDecisionResponse>.Forbidden(
                "Agent is not an approved active device.");
        }

        var command = await _agents.GetCommandByIdAsync(
            request.CommandId,
            cancellationToken);
        if (command is null ||
            command.AgentId != agent.Id ||
            command.TenantId != agent.TenantId ||
            command.EmployeeId != agent.EmployeeId.Value)
        {
            return Result<AgentCommandDecisionResponse>.NotFound(
                "Agent command not found.");
        }
        if (!string.Equals(
                command.CommandType,
                "screenshot_capture_request",
                StringComparison.Ordinal))
        {
            return Result<AgentCommandDecisionResponse>.Failure(
                "Unsupported agent command.",
                400);
        }
        if (!string.Equals(command.Status, "pending", StringComparison.Ordinal))
        {
            return Result<AgentCommandDecisionResponse>.Conflict(
                "Agent command was already answered.");
        }

        var now = _clock.UtcNow;
        if (command.ExpiresAt <= now)
        {
            command.Status = "expired";
            command.ResultCode = "consent_timeout";
            command.CompletedAt = now;
            await _uow.SaveChangesAsync(cancellationToken);
            return Result<AgentCommandDecisionResponse>.Conflict(
                "Screenshot consent request expired.");
        }

        var deviceSession = await _attendance.GetOpenDeviceSessionAsync(
            agent.Id,
            cancellationToken);
        if (deviceSession is null ||
            deviceSession.TenantId != agent.TenantId ||
            deviceSession.EmployeeId != agent.EmployeeId.Value ||
            deviceSession.DeviceId != agent.Id ||
            deviceSession.SessionEnd is not null)
        {
            return Result<AgentCommandDecisionResponse>.Conflict(
                "Screenshot consent is valid only during active work.");
        }

        command.Decision = decision;
        command.DecisionAt = now;
        command.ConsentNoticeVersion =
            request.ConsentNoticeVersion.Trim();
        if (decision == "allow")
        {
            command.Status = "accepted";
            command.ResultCode = "employee_allowed";
            command.ExpiresAt = now.Add(CaptureWindow);
        }
        else
        {
            command.Status = "denied";
            command.ResultCode = "employee_denied";
            command.CompletedAt = now;
            await _monitoring.AddConsentEventAsync(new MonitoringConsentEvent
            {
                Id = Guid.NewGuid(),
                TenantId = agent.TenantId,
                EmployeeId = agent.EmployeeId.Value,
                AgentDeviceId = agent.Id,
                IncidentId = command.Id,
                Decision = "denied",
                OccurredAt = now
            }, cancellationToken);
        }

        await _uow.SaveChangesAsync(cancellationToken);
        return Result<AgentCommandDecisionResponse>.Success(
            new AgentCommandDecisionResponse(
                command.Id,
                command.Status,
                now,
                decision == "allow" ? command.ExpiresAt : null));
    }
}

