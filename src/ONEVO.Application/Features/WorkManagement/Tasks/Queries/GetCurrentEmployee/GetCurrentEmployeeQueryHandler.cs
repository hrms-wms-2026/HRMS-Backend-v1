using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetCurrentEmployee;

public class GetCurrentEmployeeQueryHandler : IRequestHandler<GetCurrentEmployeeQuery, Result<CurrentEmployeeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;

    public GetCurrentEmployeeQueryHandler(ICurrentUser currentUser, ICallerIdentityResolver identity)
    {
        _currentUser = currentUser;
        _identity = identity;
    }

    public async Task<Result<CurrentEmployeeResponse>> Handle(GetCurrentEmployeeQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<CurrentEmployeeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<CurrentEmployeeResponse>.Forbidden("No employee record for the current user.");

        return Result<CurrentEmployeeResponse>.Success(new CurrentEmployeeResponse(callerEmployeeId.Value));
    }
}
