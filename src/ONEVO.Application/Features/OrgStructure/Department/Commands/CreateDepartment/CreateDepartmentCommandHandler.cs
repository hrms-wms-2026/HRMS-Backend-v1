using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using DepartmentEntity = ONEVO.Domain.Features.OrgStructure.Entities.Department;

namespace ONEVO.Application.Features.OrgStructure.Commands.CreateDepartment;

public class CreateDepartmentCommandHandler
    : IRequestHandler<CreateDepartmentCommand, Result<DepartmentResponse>>
{
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateDepartmentCommandHandler(
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

    public async Task<Result<DepartmentResponse>> Handle(
        CreateDepartmentCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<DepartmentResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<DepartmentResponse>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<DepartmentResponse>.NotFound("Legal entity not found.");

        // A newly created department has no positions belonging to it yet, so "the position
        // must belong to the same department" cannot be satisfied at create time (see
        // Onexo_Department_Position_User_Journey_Validation.md, Create Department section:
        // "Create the department first and assign its head afterwards, which is recommended").
        // Reject rather than silently ignoring or accepting a cross-department position.
        if (request.HeadPositionId is not null)
        {
            return Result<DepartmentResponse>.Conflict(
                "Head position cannot be assigned while creating a department. Create the department first, then assign its head position through update.");
        }

        var name = request.Name.Trim();
        var trimmedCode = request.Code?.Trim();
        var code = string.IsNullOrEmpty(trimmedCode) ? null : trimmedCode;

        if (await _departments.ExistsByNameAsync(tenantId, request.LegalEntityId, name, excludingDepartmentId: null, ct))
            return Result<DepartmentResponse>.Conflict("Department name already exists in this legal entity.");

        if (code is not null
            && await _departments.ExistsByCodeAsync(tenantId, request.LegalEntityId, code, excludingDepartmentId: null, ct))
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
        }

        var entity = new DepartmentEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = request.LegalEntityId,
            Name = name,
            Code = code,
            ParentDepartmentId = request.ParentDepartmentId,
            IsActive = true,
            CreatedAt = _dateTimeProvider.UtcNow
        };

        await _departments.AddAsync(entity, ct);
        await _departments.SaveChangesAsync(ct);

        return Result<DepartmentResponse>.Success(DepartmentMapper.ToResponse(entity));
    }
}
