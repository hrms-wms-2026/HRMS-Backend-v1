using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Queries.ListClockInPolicies;

public record ListClockInPoliciesQuery(
    Guid LegalEntityId,
    bool IncludeInactive = false) : IRequest<Result<IReadOnlyList<ClockInPolicyListItemResponse>>>;
