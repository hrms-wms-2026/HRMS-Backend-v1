using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetDepartment;

public class GetDepartmentQueryHandler
    : IRequestHandler<GetDepartmentQuery, Result<DepartmentResponse>>
{
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IRowVersionStore? _versions;

    public GetDepartmentQueryHandler(
        IDepartmentRepository departments,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser,
        IRowVersionStore? versions = null)
    {
        _departments = departments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
        _versions = versions;
    }

    public async Task<Result<DepartmentResponse>> Handle(
        GetDepartmentQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<DepartmentResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<DepartmentResponse>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<DepartmentResponse>.NotFound("Legal entity not found.");

        var entity = await _departments.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.DepartmentId, ct);
        if (entity == null)
            return Result<DepartmentResponse>.NotFound("Department not found.");

        var version = _versions is null ? null
            : await _versions.GetVersionAsync<global::ONEVO.Domain.Features.OrgStructure.Entities.Department>(entity.Id, ct);
        return Result<DepartmentResponse>.Success(DepartmentMapper.ToResponse(entity) with { Version = version });
    }
}
