using ONEVO.Application.Features.Leave.Balance.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.Mappers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Balance.Helpers;

public static class LeaveBalanceMapping
{
    public static async Task<IReadOnlyList<LeaveBalanceResponse>> MapAsync(
        ILeavePolicyRepository policies,
        Guid tenantId,
        int year,
        DateOnly asOfDate,
        IReadOnlyList<LeaveEntitlementRow> rows,
        CancellationToken ct)
    {
        var legalEntityIds = rows.Select(r => r.LegalEntityId).OfType<Guid>().Distinct().ToArray();
        var policiesByLegalEntity = await policies.ListActiveAggregatesByLegalEntityIdsAsync(
            tenantId, legalEntityIds, year, ct);

        return rows.Select(row =>
        {
            var policy = row.LegalEntityId is { } legalEntityId
                && policiesByLegalEntity.TryGetValue(legalEntityId, out var match)
                    ? match
                    : null;
            return LeaveEntitlementMapper.ToBalance(
                row,
                asOfDate,
                LeaveEntitlementPlanner.CarryExpiryFromPolicy(policy, row.Entitlement.LeaveTypeId, year));
        }).ToList();
    }
}
