using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.OutboxPayloads;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.Services;

namespace ONEVO.Application.Features.OrgStructure.Commands.ArchivePosition;

public class ArchivePositionCommandHandler
    : IRequestHandler<ArchivePositionCommand, Result<bool>>
{
    private readonly IPositionRepository _positions;
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IOutboxWriter _outboxWriter;

    public ArchivePositionCommandHandler(
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

    public async Task<Result<bool>> Handle(ArchivePositionCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<bool>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<bool>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<bool>.NotFound("Legal entity not found.");

        var existing = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.PositionId, ct);
        if (existing == null)
            return Result<bool>.NotFound("Position not found.");

        var blockers = await PositionArchiveDependencyEvaluator.EvaluateAsync(
            _positions, _departments, tenantId, request.LegalEntityId, existing.Id, ct);
        if (!PositionArchiveDependencyEvaluator.CanArchive(blockers))
        {
            return Result<bool>.Conflict(PositionArchiveDependencyEvaluator.BuildMessage(blockers));
        }

        // Archive is a soft-deactivation, never a physical delete: reporting and audit history
        // referencing this row remain intact. Child positions are not reparented automatically -
        // the blocker check above already refused to archive while active children exist.
        existing.IsActive = false;
        existing.UpdatedAt = _dateTimeProvider.UtcNow;

        _positions.Update(existing);

        await _outboxWriter.EnqueueAsync(
            OutboxMessageTypes.PositionArchived,
            new PositionOutboxPayload(existing.Id, existing.LegalEntityId!.Value, tenantId),
            tenantId,
            ct);

        await _positions.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
