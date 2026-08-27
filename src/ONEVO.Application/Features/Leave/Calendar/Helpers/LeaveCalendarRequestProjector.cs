using ONEVO.Application.Features.Leave.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Calendar.Helpers;

public sealed record LeaveCalendarAbsenceInstance(
    DateOnly Date,
    LeaveCalendarRequestRow Row,
    bool IsTentative,
    bool IsPartialCancellationHistory);

public sealed class LeaveCalendarRequestProjector
{
    public IReadOnlyList<LeaveCalendarAbsenceInstance> Project(
        IReadOnlyList<LeaveCalendarRequestRow> rows,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        bool includeTentativeBlocks)
    {
        var output = new List<LeaveCalendarAbsenceInstance>();

        foreach (var row in rows)
        {
            var request = row.Request;
            var isTentative =
                request.Status == LeaveRequestStatuses.Pending ||
                request.Status == LeaveRequestStatuses.InformationRequested;

            if (isTentative && !includeTentativeBlocks)
                continue;

            var isPartialCancellationHistory =
                request.Status == LeaveRequestStatuses.Cancelled &&
                request.PartialCancelEffectiveDate is not null;

            if (request.Status != LeaveRequestStatuses.Approved &&
                !isTentative &&
                !isPartialCancellationHistory)
            {
                continue;
            }

            var visibleStart = Max(request.StartDate, rangeStart);
            var visibleEnd = Min(request.EndDate, rangeEnd);

            if (isPartialCancellationHistory)
                visibleEnd = Min(visibleEnd, request.PartialCancelEffectiveDate!.Value.AddDays(-1));

            if (visibleEnd < visibleStart)
                continue;

            for (var date = visibleStart; date <= visibleEnd; date = date.AddDays(1))
            {
                output.Add(new LeaveCalendarAbsenceInstance(
                    date,
                    row,
                    isTentative,
                    isPartialCancellationHistory));
            }
        }

        return output
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Row.EmployeeName)
            .ThenBy(x => x.Row.Request.Id)
            .ToList();
    }

    private static DateOnly Max(DateOnly left, DateOnly right) => left > right ? left : right;

    private static DateOnly Min(DateOnly left, DateOnly right) => left < right ? left : right;
}
