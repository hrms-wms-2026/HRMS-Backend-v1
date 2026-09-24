using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListOnboardingAccessGrantRequests;

public class ListOnboardingAccessGrantRequestsQueryHandler
    : IRequestHandler<ListOnboardingAccessGrantRequestsQuery, Result<OnboardingAccessGrantRequestListPageResponse>>
{
    private static readonly Dictionary<string, string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pending"] = "Pending",
        ["approved"] = "Approved",
        ["rejected"] = "Rejected",
        ["cancelled"] = "Cancelled",
    };

    // Only the onboarding position-access action type is queryable today - AccessGrantActionType
    // defines no other constant, so any other value is rejected rather than silently ignored,
    // per the task's "must not accidentally list unrelated future access grant request types"
    // requirement.
    private static readonly Dictionary<string, string> AllowedActionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["onboarding"] = AccessGrantActionType.EmployeeOnboarding,
    };

    private readonly IAccessGrantRequestRepository _repository;
    private readonly ICurrentUser _currentUser;

    public ListOnboardingAccessGrantRequestsQueryHandler(IAccessGrantRequestRepository repository, ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<Result<OnboardingAccessGrantRequestListPageResponse>> Handle(
        ListOnboardingAccessGrantRequestsQuery request, CancellationToken ct)
    {
        var statusKey = string.IsNullOrWhiteSpace(request.Status) ? "pending" : request.Status.Trim();
        if (!AllowedStatuses.TryGetValue(statusKey, out var approvalStatus))
        {
            return Result<OnboardingAccessGrantRequestListPageResponse>.Failure(
                $"status must be one of: {string.Join(", ", AllowedStatuses.Keys)}.", 400);
        }

        var actionTypeKey = string.IsNullOrWhiteSpace(request.ActionType) ? "onboarding" : request.ActionType.Trim();
        if (!AllowedActionTypes.TryGetValue(actionTypeKey, out var actionType))
        {
            return Result<OnboardingAccessGrantRequestListPageResponse>.Failure(
                $"actionType must be one of: {string.Join(", ", AllowedActionTypes.Keys)}.", 400);
        }

        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var (items, totalCount) = await _repository.ListOnboardingRequestsAsync(
            _currentUser.TenantId, approvalStatus, actionType, request.LegalEntityId, request.RequestedRoleId,
            request.Search, page, pageSize, ct);

        return Result<OnboardingAccessGrantRequestListPageResponse>.Success(
            new OnboardingAccessGrantRequestListPageResponse(items, totalCount, page, pageSize));
    }
}
