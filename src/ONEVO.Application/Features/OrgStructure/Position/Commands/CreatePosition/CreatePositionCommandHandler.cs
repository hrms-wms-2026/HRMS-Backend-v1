using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using PositionEntity = ONEVO.Domain.Features.OrgStructure.Entities.Position;

namespace ONEVO.Application.Features.OrgStructure.Commands.CreatePosition;

public class CreatePositionCommandHandler
    : IRequestHandler<CreatePositionCommand, Result<PositionResponse>>
{
    private readonly IPositionRepository _positions;
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreatePositionCommandHandler(
        IPositionRepository positions,
        IDepartmentRepository departments,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _positions = positions;
        _departments = departments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<PositionResponse>> Handle(
        CreatePositionCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<PositionResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<PositionResponse>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<PositionResponse>.NotFound("Legal entity not found.");
        if (!legalEntity.IsActive)
            return Result<PositionResponse>.Conflict("Cannot create position: the legal entity is inactive.");

        var department = await _departments.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.DepartmentId, ct);
        if (department == null)
            return Result<PositionResponse>.NotFound("Department not found in this legal entity.");
        if (!department.IsActive)
            return Result<PositionResponse>.Conflict("Department is inactive.");

        var name = request.Name.Trim();
        var code = request.Code.Trim();

        if (await _positions.ExistsByCodeAsync(tenantId, request.LegalEntityId, code, excludingPositionId: null, ct))
            return Result<PositionResponse>.Conflict("Position code already exists in this legal entity.");

        if (await _positions.ExistsByNameAsync(tenantId, request.LegalEntityId, name, excludingPositionId: null, ct))
            return Result<PositionResponse>.Conflict("Position name already exists in this legal entity.");

        PositionEntity? reportsTo = null;
        if (request.ReportsToPositionId is { } reportsToId)
        {
            reportsTo = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, reportsToId, ct);
            if (reportsTo == null)
                return Result<PositionResponse>.NotFound("Reports-to position not found in this legal entity.");
            if (!reportsTo.IsActive)
                return Result<PositionResponse>.Conflict("Reports-to position is inactive.");
            // A new position has no Id yet, so self-reference and cycle checks are impossible
            // here - they only become reachable once the position already exists (see
            // UpdatePositionCommandHandler).
        }

        var entity = new PositionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = request.LegalEntityId,
            DepartmentId = request.DepartmentId,
            Name = name,
            Code = code,
            PositionType = request.PositionType,
            MaxOccupancy = request.MaxOccupancy,
            ReportsToPositionId = request.ReportsToPositionId,
            IsActive = true,
            CreatedAt = _dateTimeProvider.UtcNow
        };

        await _positions.AddAsync(entity, ct);
        await _positions.SaveChangesAsync(ct);

        return Result<PositionResponse>.Success(
            PositionMapper.ToResponse(entity, department.Name, reportsTo?.Name, childCount: 0));
    }
}
