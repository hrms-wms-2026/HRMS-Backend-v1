using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.SubmitCheckIn;

public record SubmitCheckInCommand(
    double? Latitude,
    double? Longitude,
    double? LocationAccuracy,
    string? LocationAddress,
    string? DeviceSerialNumber
) : IRequest<Result<CheckInResponseDto>>;
