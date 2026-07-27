using System.Text;
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Application.Features.AgentGateway.Commands.IngestBatch;

public class IngestBatchCommandHandler : IRequestHandler<IngestBatchCommand, Result>
{
    private const int MaxPayloadBytes = 512 * 1024;
    private const int MaxBatchEntries = 500;
    private static readonly HashSet<string> AllowedEventTypes =
        new(StringComparer.Ordinal)
        {
            "activity_snapshot",
            "app_usage",
            "meeting_app_usage",
            "device_session"
        };

    private readonly IAgentGatewayRepository _repo;
    private readonly ITimeAttendanceRepository _attendance;
    private readonly IUnitOfWork _uow;

    public IngestBatchCommandHandler(
        IAgentGatewayRepository repo,
        ITimeAttendanceRepository attendance,
        IUnitOfWork uow)
    {
        _repo = repo;
        _attendance = attendance;
        _uow = uow;
    }

    public async Task<Result> Handle(IngestBatchCommand request, CancellationToken cancellationToken)
    {
        var agent = await _repo.GetAgentByIdAsync(request.AgentDeviceId, cancellationToken);
        if (agent is null ||
            !string.Equals(agent.Status, "active", StringComparison.Ordinal) ||
            !agent.EmployeeId.HasValue)
            return Result.Failure("Active employee device not found.", 401);

        if (agent.TenantId != request.TenantId)
            return Result.Forbidden("Agent tenant does not match the authenticated tenant.");

        var agentSession = await _repo.GetActiveSessionByDeviceIdAsync(
            agent.DeviceId,
            cancellationToken);
        if (agentSession is null ||
            !agentSession.IsActive ||
            agentSession.TenantId != agent.TenantId ||
            agentSession.EmployeeId != agent.EmployeeId.Value)
        {
            return Result.Conflict(
                "Employee device session is not active. Sign in to the tray app again.");
        }

        var policy = await _repo.GetPolicyByAgentIdAsync(
            agent.Id,
            cancellationToken);
        if (policy is null || !IsActivityMonitoringEnabled(policy.PolicyJson))
        {
            return Result.Forbidden(
                "Activity monitoring is disabled by company policy.");
        }

        var deviceSession = await _attendance.GetOpenDeviceSessionAsync(
            agent.Id,
            cancellationToken);
        if (deviceSession is null ||
            deviceSession.TenantId != agent.TenantId ||
            deviceSession.EmployeeId != agent.EmployeeId.Value)
        {
            return Result.Conflict(
                "Monitoring requires an active clock-in session on this approved device.");
        }

        var payloadError = ValidatePayload(request.PayloadJson);
        if (payloadError is not null)
            return Result.Failure(payloadError, 400);

        await _repo.AddRawActivityBatchAsync(new ActivityRawBuffer
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            AgentDeviceId = request.AgentDeviceId,
            ReceivedAt = DateTimeOffset.UtcNow,
            PayloadJson = request.PayloadJson
        }, cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static bool IsActivityMonitoringEnabled(string policyJson)
    {
        try
        {
            using var document = JsonDocument.Parse(policyJson);
            return document.RootElement.TryGetProperty(
                       "activity_monitoring",
                       out var enabled) &&
                   enabled.ValueKind is JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ValidatePayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return "Activity batch is required.";
        if (Encoding.UTF8.GetByteCount(payloadJson) > MaxPayloadBytes)
            return "Activity batch exceeds the 512 KB limit.";

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (!document.RootElement.TryGetProperty("batch", out var batch) ||
                batch.ValueKind != JsonValueKind.Array)
                return "Activity batch must contain a batch array.";

            var count = batch.GetArrayLength();
            if (count is < 1 or > MaxBatchEntries)
                return $"Activity batch must contain between 1 and {MaxBatchEntries} events.";

            foreach (var entry in batch.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object ||
                    !entry.TryGetProperty("type", out var typeElement) ||
                    typeElement.ValueKind != JsonValueKind.String ||
                    !AllowedEventTypes.Contains(typeElement.GetString() ?? string.Empty) ||
                    !entry.TryGetProperty("data", out var data) ||
                    data.ValueKind != JsonValueKind.Object)
                {
                    return "Activity batch contains an unsupported or malformed event.";
                }

                if (!entry.TryGetProperty("event_id", out var eventIdEl) ||
                    eventIdEl.ValueKind != JsonValueKind.String ||
                    !Guid.TryParse(eventIdEl.GetString(), out var parsedEventId) ||
                    parsedEventId == Guid.Empty)
                {
                    return "Each event must include a non-empty event_id.";
                }

                if (!entry.TryGetProperty("captured_at", out var capturedAtEl) ||
                    capturedAtEl.ValueKind != JsonValueKind.String ||
                    !DateTimeOffset.TryParse(capturedAtEl.GetString(), out _))
                {
                    return "Each event must include a valid captured_at timestamp.";
                }

                if (!entry.TryGetProperty("presence_session_id", out var presenceEl) ||
                    presenceEl.ValueKind != JsonValueKind.String ||
                    !Guid.TryParse(presenceEl.GetString(), out var parsedPresenceId) ||
                    parsedPresenceId == Guid.Empty)
                {
                    return "Each event must include a non-empty presence_session_id.";
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return "Activity batch is not valid JSON.";
        }
    }
}
