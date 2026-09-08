using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.RecordTrayHeartbeat;

public sealed class RecordTrayHeartbeatCommandHandler : IRequestHandler<RecordTrayHeartbeatCommand, Result>
{
    private readonly ITrayActivationRepository _repository;
    private readonly ITrayCurrentDevice _currentDevice;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public RecordTrayHeartbeatCommandHandler(
        ITrayActivationRepository repository,
        ITrayCurrentDevice currentDevice,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _currentDevice = currentDevice;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RecordTrayHeartbeatCommand request, CancellationToken ct)
    {
        if (!_currentDevice.IsAuthenticated)
            return Result.Forbidden("Tray authentication required.");

        var device = await _repository.FindActiveDeviceAsync(
            _currentDevice.DeviceRegistrationId,
            _currentDevice.TenantId,
            ct);
        if (device is null || device.UserId != _currentDevice.UserId)
            return Result.Forbidden("Tray device is not active.");

        await _repository.UpdateDeviceLastSeenAsync(
            device.Id,
            _clock.UtcNow,
            ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
