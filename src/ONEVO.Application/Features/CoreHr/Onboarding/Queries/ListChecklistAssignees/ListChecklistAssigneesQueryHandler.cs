using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListChecklistAssignees;

public sealed class ListChecklistAssigneesQueryHandler(
    ILegalEntityRepository legalEntities,
    IPositionRepository positions,
    IPositionAssignmentRepository assignments,
    ICurrentUser currentUser)
    : IRequestHandler<ListChecklistAssigneesQuery, Result<IReadOnlyList<ChecklistAssignee>>>
{
    public async Task<Result<IReadOnlyList<ChecklistAssignee>>> Handle(
        ListChecklistAssigneesQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ChecklistAssignee>>.Forbidden("Authentication required.");

        var tenantId = currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<ChecklistAssignee>>.Forbidden("Tenant context missing.");

        var legalEntity = await legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity is null)
            return Result<IReadOnlyList<ChecklistAssignee>>.NotFound("Company not found.");

        var position = await positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.PositionId, ct);
        if (position is null)
            return Result<IReadOnlyList<ChecklistAssignee>>.NotFound("Position not found.");

        var assignees = await assignments.GetChecklistAssigneesAsync(tenantId, request.PositionId, ct);
        return Result<IReadOnlyList<ChecklistAssignee>>.Success(assignees);
    }
}
