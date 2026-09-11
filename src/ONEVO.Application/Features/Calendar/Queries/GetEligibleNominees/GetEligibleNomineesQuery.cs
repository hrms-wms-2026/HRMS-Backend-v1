using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Queries.GetEligibleNominees;

public sealed record EligibleNominee(Guid EmployeeId, string EmployeeName);
public sealed record GetEligibleNomineesResponse(IReadOnlyList<EligibleNominee> Nominees);

public sealed record GetEligibleNomineesQuery(Guid EventId) : IRequest<Result<GetEligibleNomineesResponse>>;
