using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetPositionById;

public class GetPositionByIdQueryHandler
    : IRequestHandler<GetPositionByIdQuery, Result<PositionResponse>>
{
    private readonly IPositionRepository _positions;
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public GetPositionByIdQueryHandler(
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

    public async Task<Result<PositionResponse>> Handle(
        GetPositionByIdQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<PositionResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<PositionResponse>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<PositionResponse>.NotFound("Legal entity not found.");

        var entity = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.PositionId, ct);
        if (entity == null)
            return Result<PositionResponse>.NotFound("Position not found.");

        string? departmentName = null;
        if (entity.DepartmentId is { } departmentId)
        {
            var department = await _departments.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, departmentId, ct);
            departmentName = department?.Name;
        }

        string? reportsToPositionName = null;
        if (entity.ReportsToPositionId is { } reportsToPositionId)
        {
            var reportsToPosition = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, reportsToPositionId, ct);
            reportsToPositionName = reportsToPosition?.Name;
        }

        var childCount = await _positions.CountActiveReportsToPositionAsync(tenantId, request.LegalEntityId, entity.Id, ct);

        return Result<PositionResponse>.Success(
            PositionMapper.ToResponse(entity, departmentName, reportsToPositionName, childCount));
    }
}
