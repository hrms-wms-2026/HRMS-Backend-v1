using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using CoverageRecordEntity = ONEVO.Domain.Features.OrgStructure.Entities.ManagementCoverageRecord;

namespace ONEVO.Application.Features.OrgStructure.Commands.AddManualCoverageRecord;

public class AddManualCoverageRecordCommandHandler
    : IRequestHandler<AddManualCoverageRecordCommand, Result<ManagementCoverageRecordResponse>>
{
    private readonly IPositionRepository _positions;
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AddManualCoverageRecordCommandHandler(
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

    public async Task<Result<ManagementCoverageRecordResponse>> Handle(
        AddManualCoverageRecordCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ManagementCoverageRecordResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<ManagementCoverageRecordResponse>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<ManagementCoverageRecordResponse>.NotFound("Legal entity not found.");

        var owner = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.PositionId, ct);
        if (owner == null)
            return Result<ManagementCoverageRecordResponse>.NotFound("Position not found.");
        if (!owner.IsActive)
            return Result<ManagementCoverageRecordResponse>.UnprocessableEntity("Owner position is inactive.");

        string? coveredPositionName = null;
        if (request.CoveredPositionId is { } coveredPositionId)
        {
            if (coveredPositionId == owner.Id)
                return Result<ManagementCoverageRecordResponse>.Conflict("A position cannot cover itself.");

            var coveredPosition = await _positions.GetByIdForLegalEntityAsync(
                tenantId, request.LegalEntityId, coveredPositionId, ct);
            if (coveredPosition == null)
                return Result<ManagementCoverageRecordResponse>.NotFound("Covered position not found in this legal entity.");
            if (!coveredPosition.IsActive)
                return Result<ManagementCoverageRecordResponse>.UnprocessableEntity("Covered position is inactive.");
            coveredPositionName = coveredPosition.Name;
        }

        string? coveredDepartmentName = null;
        if (request.CoveredDepartmentId is { } coveredDepartmentId)
        {
            var coveredDepartment = await _departments.GetByIdForLegalEntityAsync(
                tenantId, request.LegalEntityId, coveredDepartmentId, ct);
            if (coveredDepartment == null)
                return Result<ManagementCoverageRecordResponse>.NotFound("Covered department not found in this legal entity.");
            if (!coveredDepartment.IsActive)
                return Result<ManagementCoverageRecordResponse>.UnprocessableEntity("Covered department is inactive.");
            coveredDepartmentName = coveredDepartment.Name;
        }

        var hasConflict = await _positions.HasActiveCoverageConflictAsync(
            tenantId,
            request.LegalEntityId,
            request.CoveredTargetType,
            request.CoveredPositionId,
            request.CoveredDepartmentId,
            request.OwnerOrder,
            ct: ct);
        if (hasConflict)
            return Result<ManagementCoverageRecordResponse>.Conflict(
                $"This target already has a {ManagementCoverageRecordMapper.ResponsibilityLevelLabel(request.OwnerOrder)}.");

        // Manual coverage is always unlocked and sourced as Manual - the caller never gets to
        // pick source/isLocked directly, only the reporting-structure sync (Create/UpdatePosition
        // handlers) is allowed to write locked ReportingStructure-sourced rows.
        var record = new CoverageRecordEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = request.LegalEntityId,
            OwnerPositionId = owner.Id,
            CoveredTargetType = request.CoveredTargetType,
            CoveredPositionId = request.CoveredPositionId,
            CoveredDepartmentId = request.CoveredDepartmentId,
            OwnerOrder = request.OwnerOrder,
            Source = CoverageRecordEntity.SourceManual,
            IsLocked = false,
            Status = CoverageRecordEntity.StatusActive,
            CreatedAt = _dateTimeProvider.UtcNow
        };

        await _positions.AddManagementCoverageRecordAsync(record, ct);

        try
        {
            await _positions.SaveChangesAsync(ct);
        }
        catch (CoverageOrderConflictException)
        {
            // Belt-and-suspenders for the race where two concurrent requests both pass the
            // AnyAsync pre-check above; the DB partial unique index is the real source of truth.
            // The exception itself only knows which constraint fired, not which responsibility
            // level was requested, so the handler re-derives the same user-safe wording from
            // request.OwnerOrder rather than surfacing the exception's generic message.
            return Result<ManagementCoverageRecordResponse>.Conflict(
                $"This target already has a {ManagementCoverageRecordMapper.ResponsibilityLevelLabel(request.OwnerOrder)}.");
        }

        return Result<ManagementCoverageRecordResponse>.Success(
            ManagementCoverageRecordMapper.ToResponse(record, owner.Name, coveredPositionName, coveredDepartmentName));
    }
}
