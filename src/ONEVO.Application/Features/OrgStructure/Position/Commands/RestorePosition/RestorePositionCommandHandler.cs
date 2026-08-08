using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.OutboxPayloads;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Commands.RestorePosition;

public class RestorePositionCommandHandler
    : IRequestHandler<RestorePositionCommand, Result<bool>>
{
    private readonly IPositionRepository _positions;
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IOutboxWriter _outboxWriter;

    public RestorePositionCommandHandler(
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

    public async Task<Result<bool>> Handle(RestorePositionCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<bool>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<bool>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<bool>.NotFound("Legal entity not found.");
        if (!legalEntity.IsActive)
            return Result<bool>.UnprocessableEntity("Cannot restore: the legal entity is inactive.");

        // GetByIdForLegalEntityAsync has no IsActive filter, so this also finds
        // already-archived rows - required for restore to work at all.
        var existing = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.PositionId, ct);
        if (existing == null)
            return Result<bool>.NotFound("Position not found.");

        if (existing.IsActive)
        {
            // Already active: idempotent success, matching RestoreDepartmentCommandHandler's
            // precedent of not treating a repeat call as an error.
            return Result<bool>.Success(true);
        }

        if (existing.DepartmentId is { } departmentId)
        {
            var department = await _departments.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, departmentId, ct);
            if (department is null || !department.IsActive)
            {
                return Result<bool>.UnprocessableEntity(
                    "Cannot restore: the department is missing or inactive. Restore or reassign the department first.");
            }
        }

        if (existing.ReportsToPositionId is { } reportsToId)
        {
            var reportsTo = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, reportsToId, ct);
            if (reportsTo is null || !reportsTo.IsActive)
            {
                return Result<bool>.UnprocessableEntity(
                    "Cannot restore: the reports-to position is missing or inactive. Restore or reassign it first.");
            }
        }

        // Restore only flips IsActive. Children, code, name, and reporting line are untouched.
        existing.IsActive = true;
        existing.UpdatedAt = _dateTimeProvider.UtcNow;

        _positions.Update(existing);

        await _outboxWriter.EnqueueAsync(
            OutboxMessageTypes.PositionRestored,
            new PositionOutboxPayload(existing.Id, existing.LegalEntityId!.Value, tenantId),
            tenantId,
            ct);

        await _positions.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
