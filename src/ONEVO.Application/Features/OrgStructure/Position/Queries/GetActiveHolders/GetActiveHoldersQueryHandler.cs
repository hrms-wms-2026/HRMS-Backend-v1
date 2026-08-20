using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetActiveHolders;

public class GetActiveHoldersQueryHandler
    : IRequestHandler<GetActiveHoldersQuery, Result<IReadOnlyList<PositionActiveHolder>>>
{
    private readonly IPositionAssignmentRepository _assignments;
    private readonly IPositionRepository _positions;
    private readonly ICurrentUser _currentUser;

    public GetActiveHoldersQueryHandler(
        IPositionAssignmentRepository assignments,
        IPositionRepository positions,
        ICurrentUser currentUser)
    {
        _assignments = assignments;
        _positions = positions;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<PositionActiveHolder>>> Handle(
        GetActiveHoldersQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<PositionActiveHolder>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<PositionActiveHolder>>.Forbidden("Tenant context missing.");

        var position = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.PositionId, ct);
        if (position == null)
            return Result<IReadOnlyList<PositionActiveHolder>>.NotFound("Position not found.");

        var holders = await _assignments.GetActiveHoldersAsync(tenantId, request.PositionId, ct);
        return Result<IReadOnlyList<PositionActiveHolder>>.Success(holders);
    }
}
