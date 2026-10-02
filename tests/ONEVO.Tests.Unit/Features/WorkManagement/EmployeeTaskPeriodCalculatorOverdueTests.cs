using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class EmployeeTaskPeriodCalculatorOverdueTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 15);

    [Theory]
    [InlineData("2026-09-14", 0, false, true)]
    [InlineData("2026-09-15", 0, false, false)]
    [InlineData("2026-09-14", 100, false, false)]
    [InlineData("2026-09-14", 10, true, false)]
    public void IsOverdue_MatchesTheComputeRule(string due, int progress, bool marksComplete, bool expected)
    {
        var row = new EmployeeTaskPeriodRow(DateOnly.Parse(due), null, progress, marksComplete, null);

        EmployeeTaskPeriodCalculator.IsOverdue(row, AsOf).Should().Be(expected);
        EmployeeTaskPeriodCalculator.Compute(new[] { row }, AsOf).Overdue.Should().Be(expected ? 1 : 0);
    }

    [Fact]
    public void IsOverdue_JudgesCompletionAsOfThePeriod_SoATaskFinishedLaterWasStillOverdueThen()
    {
        var finishedAfterAsOf = new EmployeeTaskPeriodRow(
            new DateOnly(2026, 7, 1), DateTimeOffset.Parse("2026-10-05T10:00:00+00:00"), 100, true, null);

        EmployeeTaskPeriodCalculator.IsOverdue(finishedAfterAsOf, AsOf).Should().BeTrue();
        EmployeeTaskPeriodCalculator.Compute(new[] { finishedAfterAsOf }, AsOf).Completed.Should().Be(0);
        EmployeeTaskPeriodCalculator.IsOverdue(finishedAfterAsOf, new DateOnly(2026, 10, 31)).Should().BeFalse();
    }

    [Fact]
    public void DaysOverdue_CountsWholeDaysPastDue_AndIsZeroWhenNotOverdue()
    {
        EmployeeTaskPeriodCalculator.DaysOverdue(new EmployeeTaskPeriodRow(new DateOnly(2026, 9, 4), null, 0, false, null), AsOf)
            .Should().Be(11);
        EmployeeTaskPeriodCalculator.DaysOverdue(new EmployeeTaskPeriodRow(new DateOnly(2026, 9, 20), null, 0, false, null), AsOf)
            .Should().Be(0);
    }

    [Fact]
    public void IsOverdue_IsFalse_WithoutADueDate() =>
        EmployeeTaskPeriodCalculator.IsOverdue(new EmployeeTaskPeriodRow(null, null, 0, false, null), AsOf).Should().BeFalse();
}
