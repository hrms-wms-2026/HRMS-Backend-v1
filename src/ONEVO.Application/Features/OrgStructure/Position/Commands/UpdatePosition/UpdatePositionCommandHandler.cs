using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using PositionEntity = ONEVO.Domain.Features.OrgStructure.Entities.Position;

namespace ONEVO.Application.Features.OrgStructure.Commands.UpdatePosition;

public class UpdatePositionCommandHandler
    : IRequestHandler<UpdatePositionCommand, Result<PositionResponse>>
{
    private readonly IPositionRepository _positions;
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdatePositionCommandHandler(
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
        UpdatePositionCommand request, CancellationToken ct)
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
            return Result<PositionResponse>.Conflict("Cannot update position: the legal entity is inactive.");

        var existing = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.PositionId, ct);
        if (existing == null)
            return Result<PositionResponse>.NotFound("Position not found.");

        if (request.ReportsToPositionId == request.PositionId)
            return Result<PositionResponse>.Conflict("A position cannot report to itself.");

        var department = await _departments.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.DepartmentId, ct);
        if (department == null)
            return Result<PositionResponse>.NotFound("Department not found in this legal entity.");
        if (!department.IsActive)
            return Result<PositionResponse>.Conflict("Department is inactive.");

        var name = request.Name.Trim();
        var code = request.Code.Trim();

        if (await _positions.ExistsByCodeAsync(tenantId, request.LegalEntityId, code, excludingPositionId: request.PositionId, ct))
            return Result<PositionResponse>.Conflict("Position code already exists in this legal entity.");

        if (await _positions.ExistsByNameAsync(tenantId, request.LegalEntityId, name, excludingPositionId: request.PositionId, ct))
            return Result<PositionResponse>.Conflict("Position name already exists in this legal entity.");

        PositionEntity? reportsTo = null;
        if (request.ReportsToPositionId is { } reportsToId)
        {
            reportsTo = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, reportsToId, ct);
            if (reportsTo == null)
                return Result<PositionResponse>.NotFound("Reports-to position not found in this legal entity.");
            if (!reportsTo.IsActive)
                return Result<PositionResponse>.Conflict("Reports-to position is inactive.");

            var reportsToIsDescendant = await _positions.IsDescendantAsync(
                tenantId, request.LegalEntityId, existing.Id, reportsToId, ct);
            if (reportsToIsDescendant)
                return Result<PositionResponse>.Conflict("Cannot set reports-to: would create a circular reporting hierarchy.");
        }

        // Mutate the fetched entity directly; do not construct a detached replacement.
        existing.DepartmentId = request.DepartmentId;
        existing.Name = name;
        existing.Code = code;
        existing.PositionType = request.PositionType;
        existing.MaxOccupancy = request.MaxOccupancy;
        existing.ReportsToPositionId = request.ReportsToPositionId;
        existing.UpdatedAt = _dateTimeProvider.UtcNow;

        _positions.Update(existing);
        await _positions.SaveChangesAsync(ct);

        var childCount = await _positions.CountActiveReportsToPositionAsync(tenantId, request.LegalEntityId, existing.Id, ct);

        return Result<PositionResponse>.Success(
            PositionMapper.ToResponse(existing, department.Name, reportsTo?.Name, childCount));
    }
}
