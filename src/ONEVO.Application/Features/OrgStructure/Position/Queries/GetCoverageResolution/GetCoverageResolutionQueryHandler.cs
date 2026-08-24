using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetCoverageResolution;

public class GetCoverageResolutionQueryHandler
    : IRequestHandler<GetCoverageResolutionQuery, Result<IReadOnlyList<CoverageResolutionLevelResponse>>>
{
    private readonly IPositionRepository _positions;
    private readonly IPositionAssignmentRepository _assignments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public GetCoverageResolutionQueryHandler(
        IPositionRepository positions,
        IPositionAssignmentRepository assignments,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser)
    {
        _positions = positions;
        _assignments = assignments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<CoverageResolutionLevelResponse>>> Handle(
        GetCoverageResolutionQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<CoverageResolutionLevelResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<CoverageResolutionLevelResponse>>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<IReadOnlyList<CoverageResolutionLevelResponse>>.NotFound("Legal entity not found.");

        var records = await _positions.ListActiveCoverageByCoveredTargetAsync(
            tenantId, request.LegalEntityId, request.CoveredTargetType,
            request.CoveredPositionId, request.CoveredDepartmentId, excludingRecordId: null, ct);

        var ownerIds = records.Select(r => r.OwnerPositionId).Distinct().ToList();
        var owners = await _positions.GetByIdsAsync(tenantId, ownerIds, ct);
        var ownerNamesById = owners.ToDictionary(p => p.Id, p => p.Name);

        var levels = new List<CoverageResolutionLevelResponse>();
        foreach (var record in records.OrderBy(r => r.OwnerOrder))
        {
            var holders = await _assignments.GetActiveHoldersAsync(tenantId, record.OwnerPositionId, ct);
            var ownerName = ownerNamesById.GetValueOrDefault(record.OwnerPositionId);

            var resolved = holders.Count switch
            {
                1 => holders[0],
                > 1 when record.ResponsibleEmployeeId is { } chosenId =>
                    holders.FirstOrDefault(h => h.EmployeeId == chosenId),
                _ => null,
            };

            levels.Add(resolved is not null
                ? new CoverageResolutionLevelResponse(record.OwnerOrder, record.OwnerPositionId, ownerName, "Resolved", resolved.EmployeeId, $"{resolved.FirstName} {resolved.LastName}")
                : new CoverageResolutionLevelResponse(record.OwnerOrder, record.OwnerPositionId, ownerName, "Incomplete", null, null));
        }

        return Result<IReadOnlyList<CoverageResolutionLevelResponse>>.Success(levels);
    }
}
