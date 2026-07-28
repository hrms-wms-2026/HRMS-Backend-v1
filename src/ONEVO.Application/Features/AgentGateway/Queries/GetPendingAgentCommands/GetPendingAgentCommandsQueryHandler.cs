using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.AgentGateway.Queries.GetPendingAgentCommands;

public sealed class GetPendingAgentCommandsQueryHandler
    : IRequestHandler<
        GetPendingAgentCommandsQuery,
        Result<IReadOnlyList<AgentCommandDto>>>
{
    private readonly IAgentGatewayRepository _agents;
    private readonly ITimeAttendanceRepository _attendance;
    private readonly IDateTimeProvider _clock;

    public GetPendingAgentCommandsQueryHandler(
        IAgentGatewayRepository agents,
        ITimeAttendanceRepository attendance,
        IDateTimeProvider clock)
    {
        _agents = agents;
        _attendance = attendance;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<AgentCommandDto>>> Handle(
        GetPendingAgentCommandsQuery request,
        CancellationToken cancellationToken)
    {
        var agent = await _agents.GetAgentByIdAsync(
            request.AgentId,
            cancellationToken);
        if (agent is null ||
            agent.EmployeeId is null ||
            !string.Equals(agent.Status, "active", StringComparison.Ordinal))
        {
            return Result<IReadOnlyList<AgentCommandDto>>.Forbidden(
                "Agent is not an approved active device.");
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
            return Result<IReadOnlyList<AgentCommandDto>>.Success(
                Array.Empty<AgentCommandDto>());
        }

        var now = _clock.UtcNow;
        await _agents.ExpireCommandsAsync(
            agent.Id,
            now,
            cancellationToken);
        var commands = await _agents.GetPendingCommandsAsync(
            agent.Id,
            now,
            Math.Clamp(request.Limit, 1, 20),
            cancellationToken);
        var response = new List<AgentCommandDto>(commands.Count);
        foreach (var command in commands)
        {
            if (command.TenantId != agent.TenantId ||
                command.EmployeeId != agent.EmployeeId.Value)
            {
                continue;
            }

            try
            {
                using var payload = JsonDocument.Parse(command.PayloadJson);
                response.Add(new AgentCommandDto(
                    command.Id,
                    command.CommandType,
                    payload.RootElement.Clone(),
                    command.CreatedAt,
                    command.ExpiresAt,
                    command.Version));
            }
            catch (JsonException)
            {
                // Corrupt commands are deliberately withheld from the device.
            }
        }

        return Result<IReadOnlyList<AgentCommandDto>>.Success(response);
    }
}

