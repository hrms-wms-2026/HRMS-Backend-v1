using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetCoverageByTarget;

public class GetCoverageByTargetQueryHandler
    : IRequestHandler<GetCoverageByTargetQuery, Result<IReadOnlyList<ManagementCoverageRecordResponse>>>
{
    private readonly IPositionRepository _positions;
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public GetCoverageByTargetQueryHandler(
        IPositionRepository positions,
        IDepartmentRepository departments,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser)
    {
        _positions = positions;
        _departments = departments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<ManagementCoverageRecordResponse>>> Handle(
        GetCoverageByTargetQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ManagementCoverageRecordResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<ManagementCoverageRecordResponse>>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<IReadOnlyList<ManagementCoverageRecordResponse>>.NotFound("Legal entity not found.");

        var records = await _positions.ListActiveCoverageByCoveredTargetAsync(
            tenantId,
            request.LegalEntityId,
            request.CoveredTargetType,
            request.CoveredPositionId,
            request.CoveredDepartmentId,
            request.ExcludingRecordId,
            ct);

        if (records.Count == 0)
            return Result<IReadOnlyList<ManagementCoverageRecordResponse>>.Success(Array.Empty<ManagementCoverageRecordResponse>());

        // Batched name lookups (no N+1): owners can differ per record since this spans every
        // position that covers this one target, but the covered target itself is fixed - a
        // single position/department lookup covers every row.
        var ownerIds = records.Select(r => r.OwnerPositionId).Distinct().ToList();
        var owners = await _positions.GetByIdsAsync(tenantId, ownerIds, ct);
        var ownerNamesById = owners.ToDictionary(p => p.Id, p => p.Name);

        string? coveredPositionName = null;
        if (request.CoveredPositionId is { } coveredPositionId)
        {
            var coveredPosition = await _positions.GetByIdForLegalEntityAsync(
                tenantId, request.LegalEntityId, coveredPositionId, ct);
            coveredPositionName = coveredPosition?.Name;
        }

        string? coveredDepartmentName = null;
        if (request.CoveredDepartmentId is { } coveredDepartmentId)
        {
            var coveredDepartment = await _departments.GetByIdForLegalEntityAsync(
                tenantId, request.LegalEntityId, coveredDepartmentId, ct);
            coveredDepartmentName = coveredDepartment?.Name;
        }

        var responses = records
            .Select(record => ManagementCoverageRecordMapper.ToResponse(
                record,
                ownerNamesById.GetValueOrDefault(record.OwnerPositionId),
                coveredPositionName,
                coveredDepartmentName))
            .ToList();

        return Result<IReadOnlyList<ManagementCoverageRecordResponse>>.Success(responses);
    }
}
