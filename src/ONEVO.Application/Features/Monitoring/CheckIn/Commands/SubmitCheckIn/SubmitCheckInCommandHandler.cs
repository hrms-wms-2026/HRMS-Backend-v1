using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.SubmitCheckIn;

public class SubmitCheckInCommandHandler
    : IRequestHandler<SubmitCheckInCommand, Result<CheckInResponseDto>>
{
    private readonly ICheckInRepository _repository;
    private readonly ITrayCurrentDevice _device;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public SubmitCheckInCommandHandler(
        ICheckInRepository repository,
        ITrayCurrentDevice device,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _device = device;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CheckInResponseDto>> Handle(
        SubmitCheckInCommand request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var checkIn = new EmployeeCheckIn
        {
            Id = Guid.NewGuid(),
            TenantId = _device.TenantId,
            UserId = _device.UserId,
            DeviceRegistrationId = _device.DeviceRegistrationId,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            LocationAccuracy = request.LocationAccuracy,
            LocationAddress = request.LocationAddress,
            DeviceSerialNumber = request.DeviceSerialNumber,
            CheckedInAt = now,
            CreatedAt = now
        };

        await _repository.AddCheckInAsync(checkIn, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<CheckInResponseDto>.Success(new CheckInResponseDto(
            checkIn.Id,
            checkIn.CheckedInAt,
            checkIn.Latitude,
            checkIn.Longitude,
            checkIn.DeviceSerialNumber,
            FaceScanRequired: true));
    }
}
