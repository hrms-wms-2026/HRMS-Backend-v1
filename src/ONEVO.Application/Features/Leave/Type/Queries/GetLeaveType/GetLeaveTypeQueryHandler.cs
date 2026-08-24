using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Type.DTOs.Responses;
using ONEVO.Application.Features.Leave.Type.Mappers;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Type.Queries.GetLeaveType;

public class GetLeaveTypeQueryHandler : IRequestHandler<GetLeaveTypeQuery, Result<LeaveTypeResponse>>
{
    private readonly ILeaveTypeRepository _leaveTypes;
    private readonly ICurrentUser _currentUser;

    public GetLeaveTypeQueryHandler(ILeaveTypeRepository leaveTypes, ICurrentUser currentUser)
    {
        _leaveTypes = leaveTypes;
        _currentUser = currentUser;
    }

    public async Task<Result<LeaveTypeResponse>> Handle(GetLeaveTypeQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveTypeResponse>.Forbidden("Authentication required.");

        var entity = await _leaveTypes.GetByIdAsync(_currentUser.TenantId, request.LeaveTypeId, ct);
        return entity is null
            ? Result<LeaveTypeResponse>.NotFound("Leave type not found.")
            : Result<LeaveTypeResponse>.Success(LeaveTypeMapper.ToResponse(entity));
    }
}
