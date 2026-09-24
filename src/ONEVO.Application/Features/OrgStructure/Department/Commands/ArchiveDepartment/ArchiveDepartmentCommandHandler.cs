using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.Services;

namespace ONEVO.Application.Features.OrgStructure.Commands.ArchiveDepartment;

public class ArchiveDepartmentCommandHandler
    : IRequestHandler<ArchiveDepartmentCommand, Result<bool>>
{
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ArchiveDepartmentCommandHandler(
        IDepartmentRepository departments,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _departments = departments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<bool>> Handle(
        ArchiveDepartmentCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<bool>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<bool>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<bool>.NotFound("Legal entity not found.");

        var existing = await _departments.GetByIdForLegalEntityAsync(
            tenantId, request.LegalEntityId, request.DepartmentId, ct);
        if (existing == null)
            return Result<bool>.NotFound("Department not found.");

        var blockers = await DepartmentArchiveDependencyEvaluator.EvaluateAsync(
            _departments, tenantId, request.LegalEntityId, existing.Id, ct);
        if (!DepartmentArchiveDependencyEvaluator.CanArchive(blockers))
        {
            return Result<bool>.Conflict(DepartmentArchiveDependencyEvaluator.BuildMessage(blockers));
        }

        // Archive is a soft-deactivation, never a physical delete: audit history and
        // reporting-hierarchy references to this row remain intact.
        existing.IsActive = false;
        existing.UpdatedAt = _dateTimeProvider.UtcNow;

        _departments.Update(existing);
        await _departments.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
