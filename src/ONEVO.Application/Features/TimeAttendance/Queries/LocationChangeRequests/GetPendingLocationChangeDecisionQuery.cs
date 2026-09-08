using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Queries.LocationChangeRequests;

/// <summary>Tray-authenticated: is there an approved-but-not-yet-applied location change request
/// for the current employee right now? Drives the post-clock-in "save this as your new location?"
/// prompt - polled from the tray's main clocked-in screen rather than tied to any one specific
/// clock-in call, since not every clock-in path submits a check-in (camera verification gates
/// whether EmployeeCheckIn/location data exists at all).</summary>
public sealed record GetPendingLocationChangeDecisionQuery : IRequest<Result<LocationChangeRequestResponse?>>;
