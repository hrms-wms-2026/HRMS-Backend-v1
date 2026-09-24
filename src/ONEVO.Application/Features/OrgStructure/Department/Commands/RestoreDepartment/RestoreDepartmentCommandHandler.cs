using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Commands.RestoreDepartment;

public class RestoreDepartmentCommandHandler
    : IRequestHandler<RestoreDepartmentCommand, Result<bool>>
{
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RestoreDepartmentCommandHandler(
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
        RestoreDepartmentCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<bool>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<bool>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<bool>.NotFound("Legal entity not found.");

        // GetByIdForLegalEntityAsync has no IsActive filter, so this also finds
        // already-archived rows - required for restore to work at all.
        var existing = await _departments.GetByIdForLegalEntityAsync(
            tenantId, request.LegalEntityId, request.DepartmentId, ct);
        if (existing == null)
            return Result<bool>.NotFound("Department not found.");

        if (existing.IsActive)
        {
            // Already active: idempotent success, matching ArchiveDepartmentCommandHandler's
            // existing precedent of not treating a repeat call as an error.
            return Result<bool>.Success(true);
        }

        if (existing.ParentDepartmentId is { } parentId)
        {
            var parent = await _departments.GetByIdForLegalEntityAsync(
                tenantId, request.LegalEntityId, parentId, ct);
            if (parent is null || !parent.IsActive)
            {
                return Result<bool>.Conflict(
                    "Cannot restore: the parent department is missing or inactive. Restore or reassign the parent first.");
            }
        }

        // Restore only flips IsActive. Children, HeadPositionId, code, and name are untouched.
        existing.IsActive = true;
        existing.UpdatedAt = _dateTimeProvider.UtcNow;

        _departments.Update(existing);
        await _departments.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
