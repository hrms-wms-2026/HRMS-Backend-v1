using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class EmployeeTaskPeriodCalculatorTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 15);

    private static EmployeeTaskPeriodRow Row(
        string? due = null, string? completedUtc = null, int progress = 0, bool done = false, int? points = null) =>
        new(due is null ? null : DateOnly.Parse(due),
            completedUtc is null ? null : DateTimeOffset.Parse(completedUtc),
            progress, done, points);

    [Fact]
    public void Compute_BucketsEveryTaskExactlyOnce()
    {
        var rows = new[]
        {
            Row(due: "2026-09-10", completedUtc: "2026-09-09T10:00:00+00:00", done: true, progress: 100), // completed on time
            Row(due: "2026-09-10", completedUtc: "2026-09-12T10:00:00+00:00", done: true, progress: 100), // completed late
            Row(progress: 100),                                                                            // 100% but no status/due
            Row(due: "2026-09-01", progress: 40),                                                          // overdue
            Row(due: "2026-09-30", progress: 40),                                                          // in progress
            Row(due: "2026-09-30")                                                                         // not started
        };

        var s = EmployeeTaskPeriodCalculator.Compute(rows, AsOf);

        s.Assigned.Should().Be(6);
        s.Completed.Should().Be(3);
        s.Overdue.Should().Be(1);
        s.InProgress.Should().Be(1);
        s.NotStarted.Should().Be(1);
        (s.Completed + s.Overdue + s.InProgress + s.NotStarted).Should().Be(s.Assigned);
    }

    [Fact]
    public void Compute_CountsOnTimeOnlyAmongCompletedTasksThatHaveBothDates()
    {
        var rows = new[]
        {
            Row(due: "2026-09-10", completedUtc: "2026-09-10T23:00:00+00:00", done: true),  // on the due day = on time
            Row(due: "2026-09-10", completedUtc: "2026-09-11T00:30:00+00:00", done: true),  // late
            Row(completedUtc: "2026-09-05T00:00:00+00:00", done: true),                     // no due date -> excluded
            Row(due: "2026-09-10", done: true)                                              // no CompletedAt -> excluded
        };

        var s = EmployeeTaskPeriodCalculator.Compute(rows, AsOf);

        s.Completed.Should().Be(4);
        s.CompletedWithDueDate.Should().Be(2);
        s.OnTimeCompleted.Should().Be(1);
    }

    [Fact]
    public void Compute_SumsStoryPoints_TreatingNullAsZero()
    {
        var rows = new[]
        {
            Row(done: true, points: 5),
            Row(points: 8),
            Row(done: true)
        };

        var s = EmployeeTaskPeriodCalculator.Compute(rows, AsOf);

        s.StoryPointsAssigned.Should().Be(13);
        s.StoryPointsCompleted.Should().Be(5);
    }

    [Fact]
    public void Compute_OfNothing_IsAllZero()
    {
        EmployeeTaskPeriodCalculator.Compute(Array.Empty<EmployeeTaskPeriodRow>(), AsOf)
            .Should().Be(new TaskPeriodStats(0, 0, 0, 0, 0, 0, 0, 0, 0));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(18, 24, 75)]
    [InlineData(1, 3, 33)]
    [InlineData(2, 3, 67)]
    public void Percent_RoundsAndTreatsAnEmptyWholeAsZero(int part, int whole, int expected)
    {
        EmployeeTaskPeriodCalculator.Percent(part, whole).Should().Be(expected);
    }
}
