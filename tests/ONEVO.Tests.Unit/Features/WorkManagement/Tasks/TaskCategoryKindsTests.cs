using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskCategoryKindsTests
{
    [Fact]
    public void Users_pick_from_five_kinds_and_never_a_system_kind()
        => Assert.Equal(["task", "bug", "story", "feature", "testing"], TaskCategoryKinds.UserSelectable);

    [Fact]
    public void A_name_no_longer_decides_a_kind()
        => Assert.Null(typeof(TaskCategoryKinds).GetMethod("Infer"));

    [Fact]
    public void All_lists_the_seven_kinds_in_display_order()
        => Assert.Equal(["task", "bug", "story", "feature", "testing", "meeting", "checklist"], TaskCategoryKinds.All);

    [Fact]
    public void A_new_category_defaults_to_task()
        => Assert.Equal(TaskCategoryKinds.Task, new TaskCategory().Kind);
}
