using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.GetOnboardingChecklistTemplateMatches;

public class GetOnboardingChecklistTemplateMatchesQueryHandler(IChecklistTemplateRepository templateRepository, ICurrentUser currentUser)
    : IRequestHandler<GetOnboardingChecklistTemplateMatchesQuery, Result<IReadOnlyList<OnboardingChecklistTemplateMatchResponse>>>
{
    public async Task<Result<IReadOnlyList<OnboardingChecklistTemplateMatchResponse>>> Handle(
        GetOnboardingChecklistTemplateMatchesQuery request, CancellationToken ct)
    {
        var matches = await templateRepository.ListOnboardingMatchesAsync(
            currentUser.TenantId, request.LegalEntityId, request.DepartmentId, request.PositionId, ct);

        IReadOnlyList<OnboardingChecklistTemplateMatchResponse> response = matches
            .Select(m => new OnboardingChecklistTemplateMatchResponse(m.Template.Id, m.Template.Name, m.MatchLevel, m.Template.DepartmentId, m.Template.PositionId))
            .ToList();

        return Result<IReadOnlyList<OnboardingChecklistTemplateMatchResponse>>.Success(response);
    }
}
