using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.RevokeDevice;

public class RevokeDeviceCommandHandler : IRequestHandler<RevokeDeviceCommand, Result>
{
    private readonly ITrayActivationRepository _repository;
    private readonly ITrayCurrentDevice _currentDevice;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public RevokeDeviceCommandHandler(
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

    public async Task<Result> Handle(RevokeDeviceCommand request, CancellationToken ct)
    {
        if (!_currentDevice.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var now = _clock.UtcNow;

        // Best-effort pair: revoke every outstanding refresh token, then deactivate
        // the registration itself so the device can never exchange/refresh again.
        await _repository.RevokeAllRefreshTokensForDeviceAsync(
            _currentDevice.DeviceRegistrationId, "user_logout", ct);
        await _repository.DeactivateDeviceAsync(_currentDevice.DeviceRegistrationId, now, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
