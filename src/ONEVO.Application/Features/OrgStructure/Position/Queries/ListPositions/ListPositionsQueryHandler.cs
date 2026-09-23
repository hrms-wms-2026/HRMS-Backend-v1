using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Queries.ListPositions;

public class ListPositionsQueryHandler
    : IRequestHandler<ListPositionsQuery, Result<PositionPageResponse>>
{
    private readonly IPositionRepository _positions;
    private readonly IPositionAssignmentRepository _positionAssignments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public ListPositionsQueryHandler(
        IPositionRepository positions,
        IPositionAssignmentRepository positionAssignments,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser)
    {
        _positions = positions;
        _positionAssignments = positionAssignments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<PositionPageResponse>> Handle(
        ListPositionsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<PositionPageResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<PositionPageResponse>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<PositionPageResponse>.NotFound("Legal entity not found.");

        var normalizedSearch = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var sortBy = request.SortBy.Trim().ToLowerInvariant();
        var sortDirection = request.SortDirection.Trim().ToLowerInvariant();

        var page = await _positions.ListPageAsync(
            tenantId,
            request.LegalEntityId,
            request.DepartmentId,
            normalizedSearch,
            request.IncludeInactive,
            sortBy,
            sortDirection,
            request.Page,
            request.PageSize,
            ct);

        var positionIds = page.Items.Select(p => p.Id).ToList();
        var occupancyByPositionId = await _positionAssignments.GetOccupancyPreviewsAsync(
            tenantId, positionIds, PositionMapper.OccupantPreviewLimit, ct);
        var requiresApprovalByPositionId = await _positions.GetRequiresApprovalByPositionIdsAsync(
            tenantId, positionIds, ct);

        var items = page.Items
            .Select(p => PositionMapper.ToListItemResponse(p, occupancyByPositionId, requiresApprovalByPositionId))
            .ToList();
        var response = new PositionPageResponse(items, page.Page, page.PageSize, page.TotalCount, page.TotalPages);

        return Result<PositionPageResponse>.Success(response);
    }
}
