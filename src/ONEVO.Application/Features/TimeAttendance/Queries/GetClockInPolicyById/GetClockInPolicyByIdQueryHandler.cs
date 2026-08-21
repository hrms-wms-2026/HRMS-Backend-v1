using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Mappers;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Queries.GetClockInPolicyById;

public class GetClockInPolicyByIdQueryHandler
    : IRequestHandler<GetClockInPolicyByIdQuery, Result<ClockInPolicyResponse>>
{
    private readonly IClockInPolicyRepository _policies;
    private readonly ICurrentUser _currentUser;

    public GetClockInPolicyByIdQueryHandler(
        IClockInPolicyRepository policies,
        ICurrentUser currentUser)
    {
        _policies = policies;
        _currentUser = currentUser;
    }

    public async Task<Result<ClockInPolicyResponse>> Handle(
        GetClockInPolicyByIdQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ClockInPolicyResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<ClockInPolicyResponse>.Forbidden("Tenant context missing.");

        var policy = await _policies.GetByIdAsync(tenantId, request.PolicyId, ct);
        if (policy is null)
            return Result<ClockInPolicyResponse>.NotFound("Clock-in policy not found.");

        return Result<ClockInPolicyResponse>.Success(ClockInPolicyMapper.ToResponse(policy));
    }
}
