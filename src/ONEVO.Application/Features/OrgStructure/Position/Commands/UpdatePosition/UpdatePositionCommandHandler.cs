using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.OutboxPayloads;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ReportingHistoryEntity = ONEVO.Domain.Features.OrgStructure.Entities.PositionReportingHistory;
using CoverageRecordEntity = ONEVO.Domain.Features.OrgStructure.Entities.ManagementCoverageRecord;
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
    private readonly IOutboxWriter _outboxWriter;

    public UpdatePositionCommandHandler(
        IPositionRepository positions,
        IDepartmentRepository departments,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider,
        IOutboxWriter outboxWriter)
    {
        _positions = positions;
        _departments = departments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _outboxWriter = outboxWriter;
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
            return Result<PositionResponse>.UnprocessableEntity("Cannot update position: the legal entity is inactive.");

        var existing = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.PositionId, ct);
        if (existing == null)
            return Result<PositionResponse>.NotFound("Position not found.");

        if (request.ReportsToPositionId == request.PositionId)
            return Result<PositionResponse>.UnprocessableEntity("A position cannot report to itself.");

        var department = await _departments.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.DepartmentId, ct);
        if (department == null)
            return Result<PositionResponse>.NotFound("Department not found in this legal entity.");
        if (!department.IsActive)
            return Result<PositionResponse>.UnprocessableEntity("Department is inactive.");

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
                return Result<PositionResponse>.UnprocessableEntity("Reports-to position is inactive.");
            // Reporting targets may be unique or pooled positions - capacity does not disqualify
            // a position from being a valid reporting target.

            var reportsToIsDescendant = await _positions.IsDescendantAsync(
                tenantId, request.LegalEntityId, existing.Id, reportsToId, ct);
            if (reportsToIsDescendant)
                return Result<PositionResponse>.UnprocessableEntity("Cannot set reports-to: would create a circular reporting hierarchy.");
        }

        var oldReportsToPositionId = existing.ReportsToPositionId;
        var reportsToChanged = oldReportsToPositionId != request.ReportsToPositionId;

        // Mutate the fetched entity directly; do not construct a detached replacement.
        existing.DepartmentId = request.DepartmentId;
        existing.Name = name;
        existing.Code = code;
        existing.PositionType = request.MaxOccupancy == 1 ? PositionEntity.TypeUnique : PositionEntity.TypePooled;
        existing.MaxOccupancy = request.MaxOccupancy;
        existing.ReportsToPositionId = request.ReportsToPositionId;
        existing.UpdatedAt = _dateTimeProvider.UtcNow;

        _positions.Update(existing);

        if (reportsToChanged)
        {
            var today = _dateTimeProvider.Today;
            var openHistory = await _positions.GetCurrentReportingHistoryAsync(tenantId, existing.Id, ct);

            if (openHistory is not null && openHistory.EffectiveFrom == today)
            {
                // Same-day reporting change (e.g. a correction within the same day): mutate the
                // still-open row in place rather than closing it with EffectiveTo = today - 1,
                // which would produce an inverted (EffectiveTo < EffectiveFrom) interval.
                openHistory.ReportsToPositionId = request.ReportsToPositionId;
                _positions.UpdateReportingHistory(openHistory);
            }
            else
            {
                if (openHistory is not null)
                {
                    openHistory.EffectiveTo = today.AddDays(-1);
                    _positions.UpdateReportingHistory(openHistory);
                }

                await _positions.AddReportingHistoryAsync(new ReportingHistoryEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    PositionId = existing.Id,
                    ReportsToPositionId = request.ReportsToPositionId,
                    EffectiveFrom = today,
                    EffectiveTo = null,
                    CreatedAt = _dateTimeProvider.UtcNow,
                    CreatedByUserId = _currentUser.UserId
                }, ct);
            }

            CoverageRecordEntity? oldCoverage = null;
            if (oldReportsToPositionId is { } oldReportsToId)
            {
                oldCoverage = await _positions.GetLockedReportingStructureCoverageAsync(
                    tenantId, oldReportsToId, existing.Id, ct);
                if (oldCoverage is not null)
                {
                    _positions.RemoveCoverageRecord(oldCoverage);
                }
            }

            if (request.ReportsToPositionId is { } newReportsToId)
            {
                // A manual coverage rule may already claim order 1 (Primary Manager) for this
                // covered position from a different owner - the reporting-structure sync must not
                // try to insert a second active order-1 row on top of it (would violate the
                // covered-target uniqueness index). Skip the automatic sync row in that case rather
                // than failing the whole position update; the position's reports-to field itself
                // still updates normally above. The old locked row (if any) is only pending removal
                // in the change tracker at this point - it has not hit the DB yet - so it must be
                // excluded here or the query would see it as still active and always report a
                // (false) conflict.
                var hasConflict = await _positions.HasActiveCoverageConflictAsync(
                    tenantId,
                    request.LegalEntityId,
                    CoverageRecordEntity.TargetPosition,
                    existing.Id,
                    null,
                    ownerOrder: 1,
                    excludingRecordId: oldCoverage?.Id,
                    ct: ct);

                if (!hasConflict)
                {
                    await _positions.AddManagementCoverageRecordAsync(new CoverageRecordEntity
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        LegalEntityId = request.LegalEntityId,
                        OwnerPositionId = newReportsToId,
                        CoveredTargetType = CoverageRecordEntity.TargetPosition,
                        CoveredPositionId = existing.Id,
                        OwnerOrder = 1,
                        Source = CoverageRecordEntity.SourceReportingStructure,
                        IsLocked = true,
                        Status = CoverageRecordEntity.StatusActive,
                        CreatedAt = _dateTimeProvider.UtcNow
                    }, ct);
                }
            }
        }

        await _outboxWriter.EnqueueAsync(
            OutboxMessageTypes.PositionUpdated,
            new PositionOutboxPayload(existing.Id, existing.LegalEntityId!.Value, tenantId),
            tenantId,
            ct);

        await _positions.SaveChangesAsync(ct);

        var childCount = await _positions.CountActiveReportsToPositionAsync(tenantId, request.LegalEntityId, existing.Id, ct);

        return Result<PositionResponse>.Success(
            PositionMapper.ToResponse(existing, department.Name, reportsTo?.Name, childCount));
    }
}
