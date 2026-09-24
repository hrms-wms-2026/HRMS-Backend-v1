using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.Queries.ListOnboardingDrafts;

public class ListOnboardingDraftsQueryHandler : IRequestHandler<ListOnboardingDraftsQuery, Result<DraftListPageResponse>>
{
    private readonly IOnboardingDraftRepository _draftRepository;
    private readonly ICurrentUser _currentUser;

    public ListOnboardingDraftsQueryHandler(IOnboardingDraftRepository draftRepository, ICurrentUser currentUser)
    {
        _draftRepository = draftRepository;
        _currentUser = currentUser;
    }

    public async Task<Result<DraftListPageResponse>> Handle(ListOnboardingDraftsQuery request, CancellationToken ct)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        // Per the documented rule, only the creator's own drafts are shown unless the caller
        // holds employees:write (which can see and Continue Draft on behalf of others).
        var startedById = _currentUser.HasPermission("employees:write") ? (Guid?)null : _currentUser.UserId;

        var (items, totalCount) = await _draftRepository.ListWithNamesAsync(
            _currentUser.TenantId, startedById, page, pageSize, ct);

        return Result<DraftListPageResponse>.Success(new DraftListPageResponse(items, totalCount, page, pageSize));
    }
}
