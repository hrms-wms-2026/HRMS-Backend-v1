using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskEditConflictDetectorTests
{
    // Current (post-snapshot) state of the task.
    private static WorkTask Task() => new()
    {
        Title = "B", Priority = WorkTaskPriorities.Medium, EstimatedHours = 5m, ProgressPercent = 40,
        DueDate = new DateOnly(2026, 10, 9)
    };

    private static TaskEditInput Input(string title = "B", decimal? estimatedHours = 5m, int? progress = null)
        => new(title, null, WorkTaskPriorities.Medium, new DateOnly(2026, 10, 9), estimatedHours, null, progress, null, null);

    private static TaskEditLog Log(string field, object? oldValue, object? newValue, int minutesAfterSnapshot = 1) => new()
    {
        OldValuesJson = JsonSerializer.Serialize(new Dictionary<string, object?> { [field] = oldValue }),
        NewValuesJson = JsonSerializer.Serialize(new Dictionary<string, object?> { [field] = newValue }),
        ChangedAt = DateTimeOffset.UtcNow.AddMinutes(minutesAfterSnapshot)
    };

    [Fact]
    public void NoChangesByOthers_NotStale()
        => Assert.False(TaskEditConflictDetector.IsStale(Task(), Input(title: "C"), [], progressChangedAfterSnapshot: false));

    [Fact]
    public void OthersChangedADifferentField_NotStale()
    {
        // Someone moved the estimate 3 -> 5 after the snapshot. The requester's form still carries the
        // snapshot estimate (3) and only renames the task, so the two edits do not overlap.
        var task = Task();
        task.Title = "B";
        var outcome = TaskEditConflictDetector.IsStale(task, Input(title: "C", estimatedHours: 3m),
            [Log("estimatedHours", 3m, 5m)], progressChangedAfterSnapshot: false);

        Assert.False(outcome);
    }

    [Fact]
    public void OthersChangedTheSameField_Stale()
        => Assert.True(TaskEditConflictDetector.IsStale(Task(), Input(title: "C"),
            [Log("title", "A", "B")], progressChangedAfterSnapshot: false));

    [Fact]
    public void RequestLeavesTheOtherChangedFieldAtItsSnapshotValue_NotStale()
        // Snapshot title was "A"; someone renamed it to "B"; the request keeps "A" (not a change) and edits the estimate.
        => Assert.False(TaskEditConflictDetector.IsStale(Task(), Input(title: "A", estimatedHours: 9m),
            [Log("title", "A", "B")], progressChangedAfterSnapshot: false));

    [Fact]
    public void SeveralLogsOnOneField_UsesTheEarliestOldValueAsTheSnapshot()
        // A -> X -> B after the snapshot. The request keeps "A" => not a title change => not stale.
        => Assert.False(TaskEditConflictDetector.IsStale(Task(), Input(title: "A", estimatedHours: 9m),
            [Log("title", "X", "B", minutesAfterSnapshot: 5), Log("title", "A", "X", minutesAfterSnapshot: 1)],
            progressChangedAfterSnapshot: false));

    [Fact]
    public void ProgressMovedByOthers_AndRequestSetsProgress_Stale()
        => Assert.True(TaskEditConflictDetector.IsStale(Task(), Input(progress: 70), [], progressChangedAfterSnapshot: true));

    [Fact]
    public void ProgressMovedByOthers_AndRequestLeavesProgress_NotStale()
        => Assert.False(TaskEditConflictDetector.IsStale(Task(), Input(title: "C"), [], progressChangedAfterSnapshot: true));

    [Fact]
    public void NumericFormattingDifferences_AreNotChanges()
        // The log stores 5 as "5.0"; the request carries 5m. Same value, so no conflict on the estimate.
        => Assert.False(TaskEditConflictDetector.IsStale(Task(), Input(title: "B", estimatedHours: 5.0m),
            [Log("estimatedHours", 5.0m, 5m)], progressChangedAfterSnapshot: false));
}
