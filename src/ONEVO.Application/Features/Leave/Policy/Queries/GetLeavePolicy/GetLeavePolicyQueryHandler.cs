using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;
using ONEVO.Application.Features.Leave.Policy.Mappers;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Policy.Queries.GetLeavePolicy;

public class GetLeavePolicyQueryHandler : IRequestHandler<GetLeavePolicyQuery, Result<LeavePolicyResponse>>
{
    private readonly ILeavePolicyRepository _policies;
    private readonly ICurrentUser _currentUser;

    public GetLeavePolicyQueryHandler(ILeavePolicyRepository policies, ICurrentUser currentUser)
    {
        _policies = policies;
        _currentUser = currentUser;
    }

    public async Task<Result<LeavePolicyResponse>> Handle(GetLeavePolicyQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeavePolicyResponse>.Forbidden("Authentication required.");

        var aggregate = await _policies.GetAggregateByIdAsync(_currentUser.TenantId, request.LeavePolicyId, ct);
        if (aggregate is null)
            return Result<LeavePolicyResponse>.NotFound("Leave policy not found.");

        return Result<LeavePolicyResponse>.Success(LeavePolicyMapper.ToResponse(aggregate));
    }
}
