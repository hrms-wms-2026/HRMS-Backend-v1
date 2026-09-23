using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Type.DTOs.Responses;
using ONEVO.Application.Features.Leave.Type.Mappers;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Type.Queries.ListLeaveTypes;

public class ListLeaveTypesQueryHandler : IRequestHandler<ListLeaveTypesQuery, Result<IReadOnlyList<LeaveTypeResponse>>>
{
    private readonly ILeaveTypeRepository _leaveTypes;
    private readonly ICurrentUser _currentUser;

    public ListLeaveTypesQueryHandler(ILeaveTypeRepository leaveTypes, ICurrentUser currentUser)
    {
        _leaveTypes = leaveTypes;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<LeaveTypeResponse>>> Handle(ListLeaveTypesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<LeaveTypeResponse>>.Forbidden("Authentication required.");

        var types = await _leaveTypes.ListAsync(_currentUser.TenantId, request.IncludeInactive, ct);
        return Result<IReadOnlyList<LeaveTypeResponse>>.Success(types.Select(LeaveTypeMapper.ToResponse).ToList());
    }
}
