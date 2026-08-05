using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Commands.UpdateDepartment;

public class UpdateDepartmentCommandHandler
    : IRequestHandler<UpdateDepartmentCommand, Result<DepartmentResponse>>
{
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly IPositionRepository _positions;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateDepartmentCommandHandler(
        IDepartmentRepository departments,
        ILegalEntityRepository legalEntities,
        IPositionRepository positions,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _departments = departments;
        _legalEntities = legalEntities;
        _positions = positions;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<DepartmentResponse>> Handle(
        UpdateDepartmentCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<DepartmentResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<DepartmentResponse>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<DepartmentResponse>.NotFound("Legal entity not found.");

        var existing = await _departments.GetByIdForLegalEntityAsync(
            tenantId, request.LegalEntityId, request.DepartmentId, ct);
        if (existing == null)
            return Result<DepartmentResponse>.NotFound("Department not found.");

        if (request.ParentDepartmentId == request.DepartmentId)
            return Result<DepartmentResponse>.Conflict("Department cannot be its own parent.");

        var name = request.Name.Trim();
        var trimmedCode = request.Code?.Trim();
        var code = string.IsNullOrEmpty(trimmedCode) ? null : trimmedCode;

        if (await _departments.ExistsByNameAsync(
                tenantId, request.LegalEntityId, name, excludingDepartmentId: request.DepartmentId, ct))
        {
            return Result<DepartmentResponse>.Conflict("Department name already exists in this legal entity.");
        }

        if (code is not null
            && await _departments.ExistsByCodeAsync(
                tenantId, request.LegalEntityId, code, excludingDepartmentId: request.DepartmentId, ct))
        {
            return Result<DepartmentResponse>.Conflict("Department code already exists in this legal entity.");
        }

        if (request.ParentDepartmentId is { } parentId)
        {
            var parent = await _departments.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, parentId, ct);
            if (parent is null)
                return Result<DepartmentResponse>.NotFound("Parent department not found.");
            if (!parent.IsActive)
                return Result<DepartmentResponse>.Conflict("Parent department is inactive.");

            var parentIsDescendant = await _departments.IsDescendantAsync(
                tenantId, request.LegalEntityId, existing.Id, parentId, ct);
            if (parentIsDescendant)
                return Result<DepartmentResponse>.Conflict("Cannot set parent: would create a circular hierarchy.");
        }

        if (request.HeadPositionId is { } headPositionId)
        {
            var headPosition = await _positions.GetByIdForLegalEntityAsync(
                tenantId, request.LegalEntityId, headPositionId, ct);
            if (headPosition is null)
                return Result<DepartmentResponse>.NotFound("Head position not found.");
            if (!headPosition.IsActive)
                return Result<DepartmentResponse>.Conflict("Head position is inactive.");
            if (headPosition.DepartmentId != existing.Id)
                return Result<DepartmentResponse>.Conflict("Head position must belong to the department it is assigned to head.");
        }

        // Mutate the fetched entity directly; do not construct a detached replacement.
        // Full-replace PUT semantics, matching ParentDepartmentId below: the request always
        // overwrites HeadPositionId, whether it carries a value or null. A plain command record
        // can't distinguish "omitted" from "explicitly null" anyway, so omitting it clears any
        // previously-assigned head position rather than preserving it.
        existing.Name = name;
        existing.Code = code;
        existing.ParentDepartmentId = request.ParentDepartmentId;
        existing.HeadPositionId = request.HeadPositionId;
        existing.UpdatedAt = _dateTimeProvider.UtcNow;

        _departments.Update(existing);
        await _departments.SaveChangesAsync(ct);

        return Result<DepartmentResponse>.Success(DepartmentMapper.ToResponse(existing));
    }
}
