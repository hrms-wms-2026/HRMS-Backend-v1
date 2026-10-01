using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;

/// <summary>Pure shaping rules for the Work & Activity task cards. Completion and overdue follow
/// Plan 3A (EmployeeTaskPeriodCalculator) so the Overview and Work & Activity tabs agree.</summary>
public static class WorkActivityTaskRules
{
    public const int DueSoonDays = 3;
    public const int MaxItems = 5;
    public const int TrendMonths = 6;

    public static EmployeeNeedsAttentionResponse ToAttention(IReadOnlyList<EmployeeWorkTaskRow> openDueRows, DateOnly asOf)
    {
        var items = openDueRows
            .Where(r => r.DueDate.HasValue)
            .OrderBy(r => r.DueDate!.Value < asOf ? 0 : 1)
            .ThenBy(r => r.DueDate)
            .Take(MaxItems)
            .Select(r =>
            {
                var due = r.DueDate!.Value;
                var overdue = due < asOf;
                return new EmployeeAttentionItem(
                    r.Id, r.ShortId, r.Title, r.ProjectId, r.ProjectName, r.StatusName, r.StatusColor, r.StoryPoints, due,
                    overdue ? "overdue" : "due_soon",
                    overdue ? asOf.DayNumber - due.DayNumber : null,
                    overdue ? null : due.DayNumber - asOf.DayNumber);
            })
            .ToList();

        return new EmployeeNeedsAttentionResponse(asOf, openDueRows.Count(r => r.DueDate.HasValue), items);
    }

    public static EmployeeWorkTaskItem ToItem(EmployeeWorkTaskRow r) => new(
        r.Id, r.ShortId, r.Title, r.ProjectId, r.ProjectName, r.StatusName, r.StatusColor,
        r.Priority, r.StoryPoints, r.DueDate, r.ProgressPercent);

    /// <summary>[first day of the first month, first day of the month after endMonth) in UTC.</summary>
    public static (DateTimeOffset From, DateTimeOffset To) TrendWindow(DateOnly endMonthDay, int months)
    {
        var endMonth = new DateOnly(endMonthDay.Year, endMonthDay.Month, 1);
        var first = endMonth.AddMonths(-(months - 1));
        var after = endMonth.AddMonths(1);
        return (Utc(first), Utc(after));
    }

    public static IReadOnlyList<EmployeeDeliveryTrendMonth> MonthlyCompleted(
        IReadOnlyList<DateTimeOffset> completedAt, DateOnly endMonthDay, int months)
    {
        var endMonth = new DateOnly(endMonthDay.Year, endMonthDay.Month, 1);
        var counts = completedAt
            .Select(c => c.ToUniversalTime())
            .GroupBy(c => (c.Year, c.Month))
            .ToDictionary(g => g.Key, g => g.Count());

        var result = new List<EmployeeDeliveryTrendMonth>(months);
        for (var i = months - 1; i >= 0; i--)
        {
            var m = endMonth.AddMonths(-i);
            counts.TryGetValue((m.Year, m.Month), out var n);
            result.Add(new EmployeeDeliveryTrendMonth($"{m.Year:D4}-{m.Month:D2}", n));
        }
        return result;
    }

    private static DateTimeOffset Utc(DateOnly d) => new(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
