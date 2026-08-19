using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListOffboardingChecklistMatches;

public sealed record ChecklistTemplateMatchResponse(Guid Id, string Name, string MatchLevel);

public sealed record ListOffboardingChecklistMatchesQuery(Guid EmployeeId) : IRequest<Result<IReadOnlyList<ChecklistTemplateMatchResponse>>>;
