using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetPositionCoverage;

public class GetPositionCoverageQueryHandler
    : IRequestHandler<GetPositionCoverageQuery, Result<IReadOnlyList<ManagementCoverageRecordResponse>>>
{
    private readonly IPositionRepository _positions;
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public GetPositionCoverageQueryHandler(
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
        GetPositionCoverageQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ManagementCoverageRecordResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<ManagementCoverageRecordResponse>>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<IReadOnlyList<ManagementCoverageRecordResponse>>.NotFound("Legal entity not found.");

        var owner = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.PositionId, ct);
        if (owner == null)
            return Result<IReadOnlyList<ManagementCoverageRecordResponse>>.NotFound("Position not found.");

        var records = await _positions.ListCoverageByOwnerPositionAsync(tenantId, request.LegalEntityId, owner.Id, ct);

        var responses = new List<ManagementCoverageRecordResponse>(records.Count);
        foreach (var record in records)
        {
            string? coveredPositionName = null;
            if (record.CoveredPositionId is { } coveredPositionId)
            {
                var coveredPosition = await _positions.GetByIdForLegalEntityAsync(
                    tenantId, request.LegalEntityId, coveredPositionId, ct);
                coveredPositionName = coveredPosition?.Name;
            }

            string? coveredDepartmentName = null;
            if (record.CoveredDepartmentId is { } coveredDepartmentId)
            {
                var coveredDepartment = await _departments.GetByIdForLegalEntityAsync(
                    tenantId, request.LegalEntityId, coveredDepartmentId, ct);
                coveredDepartmentName = coveredDepartment?.Name;
            }

            responses.Add(ManagementCoverageRecordMapper.ToResponse(record, owner.Name, coveredPositionName, coveredDepartmentName));
        }

        return Result<IReadOnlyList<ManagementCoverageRecordResponse>>.Success(responses);
    }
}
