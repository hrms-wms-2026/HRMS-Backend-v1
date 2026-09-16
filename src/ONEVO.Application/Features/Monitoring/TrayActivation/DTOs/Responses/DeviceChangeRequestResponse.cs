namespace ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

public sealed record DeviceChangeRequestResponse(
    Guid Id,
    Guid EmployeeId,
    string RequesterDisplayName,
    string NewDeviceName,
    string NewDeviceOs,
    string Status,
    DateTimeOffset RequestedAt,
    Guid? ReviewedById,
    DateTimeOffset? ReviewedAt,
    string? ReviewComment);
