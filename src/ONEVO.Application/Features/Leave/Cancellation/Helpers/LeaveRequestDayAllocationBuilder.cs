using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Cancellation.Helpers;

public sealed record LeaveRequestDayAllocationDraft(
    DateOnly LeaveDate,
    decimal HoursUnit,
    decimal PaidHoursUnit,
    decimal UnpaidHoursUnit);

public sealed class LeaveRequestDayAllocationBuilder
{
    public IReadOnlyList<LeaveRequestDayAllocationDraft> Build(
        IReadOnlyList<DateOnly> countedDates,
        IReadOnlyList<decimal> hoursUnits,
        decimal paidHours,
        decimal unpaidHours)
    {
        if (countedDates.Count != hoursUnits.Count)
            throw new InvalidOperationException("Leave day allocations do not match the request total.");

        var paidRemaining = paidHours;
        var rows = new List<LeaveRequestDayAllocationDraft>();

        for (var i = 0; i < countedDates.Count; i++)
        {
            var unit = hoursUnits[i];
            var paid = Math.Min(unit, Math.Max(0m, paidRemaining));
            paidRemaining -= paid;
            rows.Add(new LeaveRequestDayAllocationDraft(countedDates[i], unit, paid, unit - paid));
        }

        var total = rows.Sum(x => x.HoursUnit);
        if (total != paidHours + unpaidHours)
            throw new InvalidOperationException("Leave day allocations do not match the request total.");

        return rows;
    }

    public IReadOnlyList<LeaveRequestDayAllocation> ToEntities(
        Guid tenantId,
        Guid leaveRequestId,
        IReadOnlyList<LeaveRequestDayAllocationDraft> drafts,
        DateTimeOffset now)
        => drafts.Select(draft => new LeaveRequestDayAllocation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeaveRequestId = leaveRequestId,
            LeaveDate = draft.LeaveDate,
            HoursUnit = draft.HoursUnit,
            PaidHoursUnit = draft.PaidHoursUnit,
            UnpaidHoursUnit = draft.UnpaidHoursUnit,
            Status = LeaveRequestDayAllocationStatuses.Active,
            CreatedAt = now
        }).ToList();
}
