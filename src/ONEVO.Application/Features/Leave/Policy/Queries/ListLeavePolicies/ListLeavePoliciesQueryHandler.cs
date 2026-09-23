using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;
using ONEVO.Application.Features.Leave.Policy.Mappers;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Policy.Queries.ListLeavePolicies;

public class ListLeavePoliciesQueryHandler
    : IRequestHandler<ListLeavePoliciesQuery, Result<IReadOnlyList<LeavePolicyListItemResponse>>>
{
    private readonly ILeavePolicyRepository _policies;
    private readonly ICurrentUser _currentUser;

    public ListLeavePoliciesQueryHandler(ILeavePolicyRepository policies, ICurrentUser currentUser)
    {
        _policies = policies;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<LeavePolicyListItemResponse>>> Handle(
        ListLeavePoliciesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<LeavePolicyListItemResponse>>.Forbidden("Authentication required.");

        var policies = await _policies.ListAsync(_currentUser.TenantId, request.IncludeInactive, ct);
        return Result<IReadOnlyList<LeavePolicyListItemResponse>>.Success(
            policies.Select(LeavePolicyMapper.ToListItem).ToList());
    }
}
