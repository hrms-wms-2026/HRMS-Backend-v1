using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Services;

/// <summary>
/// Builds the person-level reporting chart for a legal entity: every holder of an active
/// position gets their own card under the person they actually report to (their explicit pick,
/// else the position above), and vacant positions keep a placeholder card.
/// </summary>
public interface IPositionPeopleChartReader
{
    Task<IReadOnlyList<PositionPeopleChartNodeResponse>> BuildAsync(
        Guid tenantId, Guid legalEntityId, CancellationToken ct = default);
}
