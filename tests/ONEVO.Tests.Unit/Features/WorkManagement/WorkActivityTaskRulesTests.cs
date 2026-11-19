using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class WorkActivityTaskRulesTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 15);

    private static EmployeeWorkTaskRow Row(string shortId, string? due) => new(
        Guid.NewGuid(), shortId, shortId, Guid.NewGuid(), "Website", "In Progress", "#2563EB", false,
        "medium", 3, due is null ? null : DateOnly.Parse(due), 20, DateTimeOffset.Parse("2026-09-01T00:00:00+00:00"));

    [Fact]
    public void ToAttention_PutsOverdueFirstOldestFirst_ThenDueSoon()
    {
        var rows = new[]
        {
            Row("A", "2026-09-17"), Row("B", "2026-09-10"), Row("C", "2026-09-14"),
            Row("D", "2026-09-15"), Row("E", "2026-09-01"), Row("F", "2026-09-16")
        };

        var result = WorkActivityTaskRules.ToAttention(rows, AsOf);

        result.TotalCount.Should().Be(6);
        result.Items.Select(i => i.ShortId).Should().Equal("E", "B", "C", "D", "F", "A");
        result.Items[0].Reason.Should().Be("overdue");
        result.Items[0].OverdueDays.Should().Be(14);
        result.Items[3].Reason.Should().Be("due_soon");
        result.Items[3].OverdueDays.Should().BeNull();
    }

    [Fact]
    public void MonthlyCompleted_ReturnsSixMonthsEndingAtTheGivenMonth_WithCounts()
    {
        var completed = new[]
        {
            DateTimeOffset.Parse("2026-09-01T00:00:00+00:00"),
            DateTimeOffset.Parse("2026-09-30T23:59:59+00:00"),
            DateTimeOffset.Parse("2026-04-15T10:00:00+00:00")
        };

        var months = WorkActivityTaskRules.MonthlyCompleted(completed, new DateOnly(2026, 9, 12), 6);

        months.Select(m => m.Month).Should().Equal("2026-04", "2026-05", "2026-06", "2026-07", "2026-08", "2026-09");
        months.Select(m => m.Completed).Should().Equal(1, 0, 0, 0, 0, 2);
    }

    [Fact]
    public void TrendWindow_SpansTheFirstOfTheFirstMonthToTheFirstOfTheNextMonth()
    {
        var (from, to) = WorkActivityTaskRules.TrendWindow(new DateOnly(2026, 1, 20), 6);

        from.Should().Be(DateTimeOffset.Parse("2025-08-01T00:00:00+00:00"));
        to.Should().Be(DateTimeOffset.Parse("2026-02-01T00:00:00+00:00"));
    }
}
