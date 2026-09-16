using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Mappers;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Queries.ListWorkModes;

public class ListWorkModesQueryHandler
    : IRequestHandler<ListWorkModesQuery, Result<IReadOnlyList<WorkModeResponse>>>
{
    private readonly IWorkModeRepository _workModes;
    private readonly ICurrentUser _currentUser;

    public ListWorkModesQueryHandler(IWorkModeRepository workModes, ICurrentUser currentUser)
    {
        _workModes = workModes;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<WorkModeResponse>>> Handle(ListWorkModesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<WorkModeResponse>>.Forbidden("Authentication required.");

        var items = await _workModes.ListByLegalEntityAsync(
            _currentUser.TenantId, request.LegalEntityId, request.IncludeInactive, ct);

        return Result<IReadOnlyList<WorkModeResponse>>.Success(
            items.Select(WorkModeMapper.ToResponse).ToList());
    }
}
