using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Queries.GetClockInPolicyById;

/// <summary>
/// Tenant-scoped get by policy id (legal entity resolved from the stored row).
/// Matches the suggested GET /api/v1/attendance/clock-in-policies/{id} shape.
/// </summary>
public record GetClockInPolicyByIdQuery(Guid PolicyId)
    : IRequest<Result<ClockInPolicyResponse>>;
