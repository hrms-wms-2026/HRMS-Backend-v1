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

namespace ONEVO.Application.Features.OrgStructure.Commands.CreatePosition;

public class CreatePositionCommandHandler
    : IRequestHandler<CreatePositionCommand, Result<PositionResponse>>
{
    private readonly IPositionRepository _positions;
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IOutboxWriter _outboxWriter;

    public CreatePositionCommandHandler(
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
            return Result<PositionResponse>.UnprocessableEntity("Cannot create position: the legal entity is inactive.");

        var department = await _departments.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.DepartmentId, ct);
        if (department == null)
            return Result<PositionResponse>.NotFound("Department not found in this legal entity.");
        if (!department.IsActive)
            return Result<PositionResponse>.UnprocessableEntity("Department is inactive.");

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
                return Result<PositionResponse>.UnprocessableEntity("Reports-to position is inactive.");
            // Reporting targets may be unique or pooled positions - capacity does not disqualify
            // a position from being a valid reporting target.
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
            PositionType = request.MaxOccupancy == 1 ? PositionEntity.TypeUnique : PositionEntity.TypePooled,
            MaxOccupancy = request.MaxOccupancy,
            ReportsToPositionId = request.ReportsToPositionId,
            IsActive = true,
            CreatedAt = _dateTimeProvider.UtcNow
        };

        await _positions.AddAsync(entity, ct);

        // A reporting-history row is written unconditionally, even when ReportsToPositionId is
        // null (root position): reports_to_position_id on Position is only the current snapshot,
        // position_reporting_history is the historical source of truth and must always have one
        // open (EffectiveTo == null) row per position.
        await _positions.AddReportingHistoryAsync(new ReportingHistoryEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PositionId = entity.Id,
            ReportsToPositionId = entity.ReportsToPositionId,
            EffectiveFrom = _dateTimeProvider.Today,
            EffectiveTo = null,
            CreatedAt = _dateTimeProvider.UtcNow,
            CreatedByUserId = _currentUser.UserId
        }, ct);

        if (request.ReportsToPositionId is { } reportsToPositionId)
        {
            await _positions.AddManagementCoverageRecordAsync(new CoverageRecordEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                LegalEntityId = request.LegalEntityId,
                OwnerPositionId = reportsToPositionId,
                CoveredTargetType = CoverageRecordEntity.TargetPosition,
                CoveredPositionId = entity.Id,
                OwnerOrder = 1,
                Source = CoverageRecordEntity.SourceReportingStructure,
                IsLocked = true,
                Status = CoverageRecordEntity.StatusActive,
                CreatedAt = _dateTimeProvider.UtcNow
            }, ct);
        }

        await _outboxWriter.EnqueueAsync(
            OutboxMessageTypes.PositionCreated,
            new PositionOutboxPayload(entity.Id, entity.LegalEntityId!.Value, tenantId),
            tenantId,
            ct);

        await _positions.SaveChangesAsync(ct);

        return Result<PositionResponse>.Success(
            PositionMapper.ToResponse(entity, department.Name, reportsTo?.Name, childCount: 0));
    }
}
