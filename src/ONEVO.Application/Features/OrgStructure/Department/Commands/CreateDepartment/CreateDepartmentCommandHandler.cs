using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using DepartmentEntity = ONEVO.Domain.Features.OrgStructure.Entities.Department;
using PositionEntity = ONEVO.Domain.Features.OrgStructure.Entities.Position;

namespace ONEVO.Application.Features.OrgStructure.Commands.CreateDepartment;

public class CreateDepartmentCommandHandler
    : IRequestHandler<CreateDepartmentCommand, Result<DepartmentResponse>>
{
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly IPositionRepository _positions;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateDepartmentCommandHandler(
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

        // A newly created department has no positions of its own yet, so a head chosen at
        // create time is an existing position that moves into the new department (option 2 in
        // Onexo_Department_Position_User_Journey_Validation.md, Create Department section). The
        // UI warns about the move; a position already heading another department is refused.
        PositionEntity? headPosition = null;
        if (request.HeadPositionId is { } headPositionId)
        {
            headPosition = await _positions.GetByIdForLegalEntityAsync(
                tenantId, request.LegalEntityId, headPositionId, ct);
            if (headPosition is null)
                return Result<DepartmentResponse>.NotFound("Head position not found.");
            if (!headPosition.IsActive)
                return Result<DepartmentResponse>.Conflict("Head position is inactive.");
            if (await _positions.CountHeadDepartmentReferencesAsync(
                    tenantId, request.LegalEntityId, headPositionId, ct) > 0)
            {
                return Result<DepartmentResponse>.Conflict(
                    "This position already heads another department. Choose a different head position.");
            }
        }

        var entity = new DepartmentEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = request.LegalEntityId,
            Name = name,
            Code = code,
            ParentDepartmentId = request.ParentDepartmentId,
            HeadPositionId = headPosition?.Id,
            IsActive = true,
            CreatedAt = _dateTimeProvider.UtcNow
        };

        await _departments.AddAsync(entity, ct);

        if (headPosition is not null)
        {
            headPosition.DepartmentId = entity.Id;
            headPosition.UpdatedAt = _dateTimeProvider.UtcNow;
            _positions.Update(headPosition);
        }

        await _departments.SaveChangesAsync(ct);

        return Result<DepartmentResponse>.Success(DepartmentMapper.ToResponse(entity));
    }
}
