using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.Queries.GetOnboardingDraft;

public class GetOnboardingDraftQueryHandler : IRequestHandler<GetOnboardingDraftQuery, Result<OnboardingDraftResponse>>
{
    private readonly IOnboardingDraftRepository _draftRepository;
    private readonly ICurrentUser _currentUser;

    public GetOnboardingDraftQueryHandler(IOnboardingDraftRepository draftRepository, ICurrentUser currentUser)
    {
        _draftRepository = draftRepository;
        _currentUser = currentUser;
    }

    public async Task<Result<OnboardingDraftResponse>> Handle(GetOnboardingDraftQuery request, CancellationToken ct)
    {
        var draft = await _draftRepository.GetTrackedAsync(_currentUser.TenantId, request.DraftId, ct);
        if (draft is null)
        {
            return Result<OnboardingDraftResponse>.NotFound(
                "The employee or selected organization record could not be found.");
        }

        if (draft.StartedById != _currentUser.UserId && !_currentUser.HasPermission("employees:write"))
        {
            return Result<OnboardingDraftResponse>.Forbidden();
        }

        var response = await _draftRepository.GetResponseByIdAsync(_currentUser.TenantId, request.DraftId, ct);
        return Result<OnboardingDraftResponse>.Success(response!);
    }
}
