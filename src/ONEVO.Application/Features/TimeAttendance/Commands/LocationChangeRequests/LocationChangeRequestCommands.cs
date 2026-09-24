using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Commands.LocationChangeRequests;

public sealed record CreateLocationChangeRequestCommand(
    double Latitude,
    double Longitude,
    double? AccuracyMeters,
    string Reason) : IRequest<Result<LocationChangeRequestResponse>>;

public sealed record ApproveLocationChangeRequestCommand(
    Guid Id, string? ReviewComment) : IRequest<Result<LocationChangeRequestResponse>>;

public sealed record RejectLocationChangeRequestCommand(
    Guid Id, string? ReviewComment) : IRequest<Result<LocationChangeRequestResponse>>;

public sealed record CancelLocationChangeRequestCommand(Guid Id) : IRequest<Result<LocationChangeRequestResponse>>;

/// <summary>The employee's answer to the "save this as your new location?" prompt shown after a
/// clock-in while an approved request is outstanding. Apply = true updates EmployeeWorkLocation
/// and marks the request applied (terminal); Apply = false leaves everything as-is so the same
/// request re-prompts on the next clock-in.</summary>
public sealed record RespondToLocationChangeRequestCommand(
    Guid Id, bool Apply) : IRequest<Result<LocationChangeRequestResponse>>;
