using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Queries.GetClockInPolicy;

public record GetClockInPolicyQuery(Guid LegalEntityId, Guid PolicyId)
    : IRequest<Result<ClockInPolicyResponse>>;
