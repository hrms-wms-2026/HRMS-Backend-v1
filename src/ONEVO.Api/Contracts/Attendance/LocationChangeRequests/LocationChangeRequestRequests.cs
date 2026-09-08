namespace ONEVO.Api.Contracts.Attendance.LocationChangeRequests;

public sealed record LocationChangeRequestRequest(
    double Latitude, double Longitude, double? AccuracyMeters, string Reason);

public sealed record ReviewLocationChangeRequestRequest(string? ReviewComment);

public sealed record RespondToLocationChangeRequestRequest(bool Apply);
