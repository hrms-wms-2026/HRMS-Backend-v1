namespace ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

public sealed record LocationChangeRequestResponse(
    Guid Id,
    Guid EmployeeId,
    string RequesterDisplayName,
    double RequestedLatitude,
    double RequestedLongitude,
    double? RequestedAccuracyMeters,
    string Reason,
    string Status,
    DateTimeOffset RequestedAt,
    Guid? ReviewedById,
    DateTimeOffset? ReviewedAt,
    string? ReviewComment,
    DateTimeOffset? AppliedAt);
