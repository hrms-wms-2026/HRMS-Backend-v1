using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Balance.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Balance.Queries.GetMyLeaveWorkWindow;

public sealed class GetMyLeaveWorkWindowQueryHandler
    : IRequestHandler<GetMyLeaveWorkWindowQuery, Result<LeaveWorkWindowResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IEmployeeRepository _employees;
    private readonly ILegalEntityRepository _legalEntities;

    public GetMyLeaveWorkWindowQueryHandler(
        ICurrentUser currentUser,
        IEmployeeRepository employees,
        ILegalEntityRepository legalEntities)
    {
        _currentUser = currentUser;
        _employees = employees;
        _legalEntities = legalEntities;
    }

    public async Task<Result<LeaveWorkWindowResponse>> Handle(
        GetMyLeaveWorkWindowQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveWorkWindowResponse>.Forbidden("Authentication required.");
        if (_currentUser.TenantId == Guid.Empty)
            return Result<LeaveWorkWindowResponse>.Forbidden("Tenant context missing.");

        var employee = await _employees.GetByUserIdAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result<LeaveWorkWindowResponse>.NotFound(LeaveEntitlementMessages.NoEmployeeRecord);

        if (employee.LegalEntityId is not Guid legalEntityId)
            return Result<LeaveWorkWindowResponse>.Success(new LeaveWorkWindowResponse(null, null, null, null));

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(_currentUser.TenantId, legalEntityId, ct);
        if (legalEntity is null)
            return Result<LeaveWorkWindowResponse>.Success(new LeaveWorkWindowResponse(null, null, null, null));

        return Result<LeaveWorkWindowResponse>.Success(new LeaveWorkWindowResponse(
            legalEntity.WorkStartTime,
            legalEntity.WorkEndTime,
            legalEntity.BreakDurationMinutes,
            legalEntity.Timezone));
    }
}
