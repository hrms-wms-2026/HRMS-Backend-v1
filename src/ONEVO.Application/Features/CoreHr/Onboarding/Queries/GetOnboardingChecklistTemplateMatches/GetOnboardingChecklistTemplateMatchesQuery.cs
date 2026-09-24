using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.GetOnboardingChecklistTemplateMatches;

public record GetOnboardingChecklistTemplateMatchesQuery(Guid LegalEntityId, Guid? DepartmentId, Guid? PositionId)
    : IRequest<Result<IReadOnlyList<OnboardingChecklistTemplateMatchResponse>>>;
