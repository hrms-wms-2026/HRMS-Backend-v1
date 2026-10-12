using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public sealed class TaskProgressClassifierTests
{
    private static readonly DateOnly Today = new(2026, 9, 29);

    [Theory]
    [InlineData(true, 0, null, TaskProgressBucket.Completed)]            // complete column wins
    [InlineData(false, 100, "2026-09-01", TaskProgressBucket.Completed)] // 100% is complete even past due
    [InlineData(false, 40, "2026-09-28", TaskProgressBucket.Overdue)]    // overdue beats in-progress
    [InlineData(false, 0, "2026-09-29", TaskProgressBucket.NotStarted)]  // due today is NOT overdue
    [InlineData(false, 10, "2026-10-05", TaskProgressBucket.InProgress)]
    [InlineData(false, 0, null, TaskProgressBucket.NotStarted)]
    public void Classifies_exactly_like_the_task_progress_widget(bool marksComplete, int progress, string? due, TaskProgressBucket expected)
    {
        var dueDate = due is null ? (DateOnly?)null : DateOnly.Parse(due);
        Assert.Equal(expected, TaskProgressClassifier.Classify(marksComplete, progress, dueDate, Today));
    }
}
