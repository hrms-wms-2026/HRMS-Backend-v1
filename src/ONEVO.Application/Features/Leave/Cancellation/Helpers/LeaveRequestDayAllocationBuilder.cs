using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Cancellation.Helpers;

public sealed record LeaveRequestDayAllocationDraft(
    DateOnly LeaveDate,
    decimal DayUnit,
    decimal PaidUnit,
    decimal UnpaidUnit);

public sealed class LeaveRequestDayAllocationBuilder
{
    public IReadOnlyList<LeaveRequestDayAllocationDraft> Build(
        IReadOnlyList<DateOnly> countedDates,
        string? halfDayPeriod,
        decimal paidDays,
        decimal unpaidDays)
    {
        var paidRemaining = paidDays;
        var rows = new List<LeaveRequestDayAllocationDraft>();

        foreach (var date in countedDates)
        {
            var unit = !string.IsNullOrWhiteSpace(halfDayPeriod) && countedDates.Count == 1
                ? 0.5m
                : 1m;
            var paid = Math.Min(unit, Math.Max(0m, paidRemaining));
            paidRemaining -= paid;
            rows.Add(new LeaveRequestDayAllocationDraft(date, unit, paid, unit - paid));
        }

        var total = rows.Sum(x => x.DayUnit);
        if (total != paidDays + unpaidDays)
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
            DayUnit = draft.DayUnit,
            PaidUnit = draft.PaidUnit,
            UnpaidUnit = draft.UnpaidUnit,
            Status = LeaveRequestDayAllocationStatuses.Active,
            CreatedAt = now
        }).ToList();
}
