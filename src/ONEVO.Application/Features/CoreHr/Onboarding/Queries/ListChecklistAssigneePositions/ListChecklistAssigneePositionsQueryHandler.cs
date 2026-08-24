using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListChecklistAssigneePositions;

public sealed class ListChecklistAssigneePositionsQueryHandler(
    ILegalEntityRepository legalEntities,
    IPositionRepository positions,
    ICurrentUser currentUser)
    : IRequestHandler<ListChecklistAssigneePositionsQuery, Result<IReadOnlyList<ChecklistAssigneePosition>>>
{
    public async Task<Result<IReadOnlyList<ChecklistAssigneePosition>>> Handle(
        ListChecklistAssigneePositionsQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ChecklistAssigneePosition>>.Forbidden("Authentication required.");

        var tenantId = currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<ChecklistAssigneePosition>>.Forbidden("Tenant context missing.");

        var legalEntity = await legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity is null)
            return Result<IReadOnlyList<ChecklistAssigneePosition>>.NotFound("Company not found.");

        var items = await positions.ListByLegalEntityAsync(
            tenantId, request.LegalEntityId, includeInactive: false, departmentId: null, ct);

        var response = items
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new ChecklistAssigneePosition(p.Id, p.Name))
            .ToList();

        return Result<IReadOnlyList<ChecklistAssigneePosition>>.Success(response);
    }
}
