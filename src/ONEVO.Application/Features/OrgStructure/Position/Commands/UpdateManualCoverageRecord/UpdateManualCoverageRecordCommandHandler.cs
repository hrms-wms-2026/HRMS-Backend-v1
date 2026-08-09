using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using CoverageRecordEntity = ONEVO.Domain.Features.OrgStructure.Entities.ManagementCoverageRecord;

namespace ONEVO.Application.Features.OrgStructure.Commands.UpdateManualCoverageRecord;

public class UpdateManualCoverageRecordCommandHandler
    : IRequestHandler<UpdateManualCoverageRecordCommand, Result<ManagementCoverageRecordResponse>>
{
    private readonly IPositionRepository _positions;
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateManualCoverageRecordCommandHandler(
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
        UpdateManualCoverageRecordCommand request, CancellationToken ct)
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

        var record = await _positions.GetCoverageRecordByIdAsync(tenantId, request.CoverageId, ct);
        // A record found under a different owner/legal entity than the route specifies is treated
        // the same as "not found" here, rather than leaking its existence via a different error -
        // same convention as RemoveManualCoverageRecordCommandHandler.
        if (record == null || record.OwnerPositionId != owner.Id || record.LegalEntityId != request.LegalEntityId)
            return Result<ManagementCoverageRecordResponse>.NotFound("Coverage record not found.");

        if (record.IsLocked || record.Source != CoverageRecordEntity.SourceManual)
        {
            return Result<ManagementCoverageRecordResponse>.Conflict(
                "This access comes from the reporting structure. Change the position's Reports to value to modify it.");
        }

        var hasConflict = await _positions.HasActiveCoverageConflictAsync(
            tenantId,
            request.LegalEntityId,
            record.CoveredTargetType,
            record.CoveredPositionId,
            record.CoveredDepartmentId,
            request.OwnerOrder,
            excludingRecordId: record.Id,
            ct: ct);
        if (hasConflict)
            return Result<ManagementCoverageRecordResponse>.Conflict(
                "An active coverage record already exists for this covered target at this responsibility level.");

        record.OwnerOrder = request.OwnerOrder;
        record.UpdatedAt = _dateTimeProvider.UtcNow;
        _positions.UpdateCoverageRecord(record);

        try
        {
            await _positions.SaveChangesAsync(ct);
        }
        catch (CoverageOrderConflictException ex)
        {
            // Belt-and-suspenders for the race where two concurrent requests both pass the
            // AnyAsync pre-check above; the DB partial unique index is the real source of truth.
            return Result<ManagementCoverageRecordResponse>.Conflict(ex.Message);
        }

        string? coveredPositionName = null;
        if (record.CoveredPositionId is { } coveredPositionId)
        {
            var coveredPosition = await _positions.GetByIdForLegalEntityAsync(
                tenantId, request.LegalEntityId, coveredPositionId, ct);
            coveredPositionName = coveredPosition?.Name;
        }

        string? coveredDepartmentName = null;
        if (record.CoveredDepartmentId is { } coveredDepartmentId)
        {
            var coveredDepartment = await _departments.GetByIdForLegalEntityAsync(
                tenantId, request.LegalEntityId, coveredDepartmentId, ct);
            coveredDepartmentName = coveredDepartment?.Name;
        }

        return Result<ManagementCoverageRecordResponse>.Success(
            ManagementCoverageRecordMapper.ToResponse(record, owner.Name, coveredPositionName, coveredDepartmentName));
    }
}
