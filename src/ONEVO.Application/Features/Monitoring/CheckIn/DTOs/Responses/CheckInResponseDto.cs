namespace ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;

public record CheckInResponseDto(
    Guid CheckInId,
    DateTimeOffset CheckedInAt,
    double? Latitude,
    double? Longitude,
    string? DeviceSerialNumber,
    bool FaceScanRequired);
