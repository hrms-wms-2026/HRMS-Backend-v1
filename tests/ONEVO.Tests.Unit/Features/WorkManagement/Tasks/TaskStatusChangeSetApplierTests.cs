using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskStatusChangeSetApplierTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ToDo = Guid.NewGuid();
    private static readonly Guid InProcess = Guid.NewGuid();
    private static readonly Guid Review = Guid.NewGuid();
    private static readonly Guid Complete = Guid.NewGuid();

    private static List<TaskStatusEntity> Current() =>
    [
        Status(ToDo, "To Do", 0, TaskStatusCategories.NotStarted, "#94A3B8"),
        Status(InProcess, "In Process", 1, TaskStatusCategories.Active, "#2563EB"),
        Status(Review, "Review", 2, TaskStatusCategories.Active, "#7C3AED"),
        Status(Complete, "Complete", 3, TaskStatusCategories.Done, "#16A34A"),
    ];

    private static TaskStatusEntity Status(Guid id, string name, int order, string category, string color) => new()
    {
        Id = id, TenantId = TenantId, ProjectId = ProjectId, Name = name, DisplayOrder = order,
        Category = category, Color = color, Visibility = TaskStatusVisibilities.Public,
        MarksTaskComplete = category == TaskStatusCategories.Done, CreatedAt = DateTimeOffset.UtcNow
    };

    private static List<Guid> BaseOrder => [ToDo, InProcess, Review, Complete];

    private static List<string> Order(params object[] keys) => keys.Select(k => k.ToString()!).ToList();

    private static TaskStatusChangeSet Set(
        List<TaskStatusAddChange>? adds = null, List<TaskStatusUpdateChange>? updates = null,
        List<TaskStatusDeleteChange>? deletes = null, List<string>? order = null)
        => new(adds ?? [], updates ?? [], deletes ?? [], BaseOrder, order ?? Order(ToDo, InProcess, Review, Complete));

    private static TaskStatusChangeApplyResult Apply(List<TaskStatusEntity> current, TaskStatusChangeSet set)
        => TaskStatusChangeSetApplier.Apply(current, set, TenantId, ProjectId, UserId, DateTimeOffset.UtcNow);

    private static TaskStatusUpdateChange Rename(Guid id, string from, string to, string category, string color)
        => new(id, new TaskStatusSnapshot(from, category, color, "public"), new TaskStatusSnapshot(to, category, color, "public"));

    [Fact]
    public void Rename_matching_from_snapshot_is_applied()
    {
        var result = Apply(Current(), Set(updates: [Rename(Review, "Review", "QA", TaskStatusCategories.Active, "#7C3AED")]));

        Assert.Equal(TaskStatusChangeApplyOutcome.Applied, result.Outcome);
        Assert.Equal("QA", result.FinalOrder.Single(s => s.Id == Review).Name);
    }

    [Fact]
    public void Rename_is_stale_when_live_name_differs_from_snapshot()
    {
        var current = Current();
        current.Single(s => s.Id == Review).Name = "Code Review";

        var result = Apply(current, Set(updates: [Rename(Review, "Review", "QA", TaskStatusCategories.Active, "#7C3AED")]));

        Assert.Equal(TaskStatusChangeApplyOutcome.Stale, result.Outcome);
        Assert.Equal("Code Review", current.Single(s => s.Id == Review).Name);
    }

    [Fact]
    public void Delete_of_a_status_that_no_longer_exists_is_stale()
    {
        var current = Current().Where(s => s.Id != Review).ToList();

        var result = Apply(current, Set(deletes: [new TaskStatusDeleteChange(Review, "Review")],
            order: Order(ToDo, InProcess, Complete)));

        Assert.Equal(TaskStatusChangeApplyOutcome.Stale, result.Outcome);
    }

    [Fact]
    public void Added_status_is_inserted_after_its_requested_predecessor_without_counting_as_a_reorder()
    {
        var set = Set(
            adds: [new TaskStatusAddChange("new-1", "Blocked", TaskStatusCategories.Active, "#DC2626", "public")],
            order: Order(ToDo, InProcess, "new-1", Review, Complete));

        Assert.False(set.ReordersExisting);
        var result = Apply(Current(), set);

        Assert.Equal(TaskStatusChangeApplyOutcome.Applied, result.Outcome);
        Assert.Equal(["To Do", "In Process", "Blocked", "Review", "Complete"], result.FinalOrder.Select(s => s.Name));
        Assert.Equal([0, 1, 2, 3, 4], result.FinalOrder.Select(s => s.DisplayOrder));
        Assert.Single(result.Added);
    }

    [Fact]
    public void Reorder_is_applied_and_keeps_statuses_added_by_others_meanwhile()
    {
        var current = Current();
        current.Add(Status(Guid.NewGuid(), "Someone Else's", 4, TaskStatusCategories.Active, "#000000"));

        var result = Apply(current, Set(order: Order(ToDo, Review, InProcess, Complete)));

        Assert.Equal(TaskStatusChangeApplyOutcome.Applied, result.Outcome);
        Assert.Equal(["To Do", "Review", "In Process", "Complete", "Someone Else's"], result.FinalOrder.Select(s => s.Name));
    }

    [Fact]
    public void Reorder_is_stale_when_live_order_changed_since_the_request()
    {
        var current = Current();
        current.Single(s => s.Id == InProcess).DisplayOrder = 2;
        current.Single(s => s.Id == Review).DisplayOrder = 1;

        var result = Apply(current, Set(order: Order(Review, ToDo, InProcess, Complete)));

        Assert.Equal(TaskStatusChangeApplyOutcome.Stale, result.Outcome);
    }

    [Fact]
    public void Second_done_status_is_invalid()
    {
        var result = Apply(Current(), Set(
            adds: [new TaskStatusAddChange("new-1", "Shipped", TaskStatusCategories.Done, "#16A34A", "public")],
            order: Order(ToDo, InProcess, Review, Complete, "new-1")));

        Assert.Equal(TaskStatusChangeApplyOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public void Duplicate_names_are_invalid()
    {
        var result = Apply(Current(), Set(updates: [Rename(Review, "Review", "in process", TaskStatusCategories.Active, "#7C3AED")]));

        Assert.Equal(TaskStatusChangeApplyOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public void Deleting_the_last_active_status_is_invalid()
    {
        var result = Apply(Current(), Set(
            deletes: [new TaskStatusDeleteChange(InProcess, "In Process"), new TaskStatusDeleteChange(Review, "Review")],
            order: Order(ToDo, Complete)));

        Assert.Equal(TaskStatusChangeApplyOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public void Footprints_conflict_on_shared_status_or_when_both_reorder()
    {
        var renameReview = Set(updates: [Rename(Review, "Review", "QA", TaskStatusCategories.Active, "#7C3AED")]).Footprint();
        var deleteReview = Set(deletes: [new TaskStatusDeleteChange(Review, "Review")], order: Order(ToDo, InProcess, Complete)).Footprint();
        var renameToDo = Set(updates: [Rename(ToDo, "To Do", "Backlog", TaskStatusCategories.NotStarted, "#94A3B8")]).Footprint();
        var reorderA = Set(order: Order(InProcess, ToDo, Review, Complete)).Footprint();
        var reorderB = Set(order: Order(ToDo, Review, InProcess, Complete)).Footprint();
        var addOnly = Set(
            adds: [new TaskStatusAddChange("new-1", "Blocked", TaskStatusCategories.Active, "#DC2626", "public")],
            order: Order(ToDo, InProcess, "new-1", Review, Complete)).Footprint();

        Assert.True(renameReview.ConflictsWith(deleteReview));
        Assert.False(renameReview.ConflictsWith(renameToDo));
        Assert.True(reorderA.ConflictsWith(reorderB));
        Assert.False(reorderA.ConflictsWith(renameToDo));
        Assert.True(addOnly.IsEmpty);
        Assert.False(addOnly.ConflictsWith(reorderA));
    }
}
