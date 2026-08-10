using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Screenshots.Mappers;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Queries.GetPendingCommands;

public class GetPendingCommandsQueryHandler
    : IRequestHandler<GetPendingCommandsQuery, Result<List<AgentCommandDto>>>
{
    private readonly IAgentCommandRepository _commands;
    private readonly ITrayCurrentDevice _device;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public GetPendingCommandsQueryHandler(
        IAgentCommandRepository commands,
        ITrayCurrentDevice device,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _commands = commands;
        _device = device;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<List<AgentCommandDto>>> Handle(
        GetPendingCommandsQuery request,
        CancellationToken cancellationToken)
    {
        var pending = await _commands.GetPendingForDeviceAsync(_device.DeviceRegistrationId, cancellationToken);

        if (pending.Count == 0)
            return Result<List<AgentCommandDto>>.Success([]);

        var now = _clock.UtcNow;
        foreach (var cmd in pending)
        {
            cmd.Status = "delivered";
            cmd.DeliveredAt = now;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<List<AgentCommandDto>>.Success(
            pending.Select(AgentCommandMapper.ToDto).ToList());
    }
}
