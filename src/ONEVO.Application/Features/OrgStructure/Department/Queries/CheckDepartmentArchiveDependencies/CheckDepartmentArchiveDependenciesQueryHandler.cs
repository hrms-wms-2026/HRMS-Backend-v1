using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.Services;

namespace ONEVO.Application.Features.OrgStructure.Queries.CheckDepartmentArchiveDependencies;

public class CheckDepartmentArchiveDependenciesQueryHandler
    : IRequestHandler<CheckDepartmentArchiveDependenciesQuery, Result<DepartmentArchiveDependencyResponse>>
{
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public CheckDepartmentArchiveDependenciesQueryHandler(
        IDepartmentRepository departments,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser)
    {
        _departments = departments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<DepartmentArchiveDependencyResponse>> Handle(
        CheckDepartmentArchiveDependenciesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<DepartmentArchiveDependencyResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<DepartmentArchiveDependencyResponse>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<DepartmentArchiveDependencyResponse>.NotFound("Legal entity not found.");

        var department = await _departments.GetByIdForLegalEntityAsync(
            tenantId, request.LegalEntityId, request.DepartmentId, ct);
        if (department == null)
            return Result<DepartmentArchiveDependencyResponse>.NotFound("Department not found.");

        var blockers = await DepartmentArchiveDependencyEvaluator.EvaluateAsync(
            _departments, tenantId, request.LegalEntityId, department.Id, ct);

        var response = new DepartmentArchiveDependencyResponse(
            department.Id,
            DepartmentArchiveDependencyEvaluator.CanArchive(blockers),
            blockers,
            DepartmentArchiveDependencyEvaluator.BuildMessage(blockers));

        return Result<DepartmentArchiveDependencyResponse>.Success(response);
    }
}
