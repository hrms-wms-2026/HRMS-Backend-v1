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
        decimal paidHours,
        decimal unpaidHours)
    {
        var paidRemaining = paidHours;
        var rows = new List<LeaveRequestDayAllocationDraft>();

        foreach (var date in countedDates)
        {
            const decimal unit = 1m;
            var paid = Math.Min(unit, Math.Max(0m, paidRemaining));
            paidRemaining -= paid;
            rows.Add(new LeaveRequestDayAllocationDraft(date, unit, paid, unit - paid));
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
