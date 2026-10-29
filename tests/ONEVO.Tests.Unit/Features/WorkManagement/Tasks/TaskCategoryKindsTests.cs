using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskCategoryKindsTests
{
    [Theory]
    [InlineData("Bug", null, TaskCategoryKinds.Bug)]
    [InlineData(" defects ", null, TaskCategoryKinds.Bug)]
    [InlineData("User Story", null, TaskCategoryKinds.Story)]
    [InlineData("stories", null, TaskCategoryKinds.Story)]
    [InlineData("QA", null, TaskCategoryKinds.Testing)]
    [InlineData("Testing", null, TaskCategoryKinds.Testing)]
    [InlineData("Enhancement", null, TaskCategoryKinds.Feature)]
    [InlineData("feature", null, TaskCategoryKinds.Feature)]
    [InlineData("Onboarding", null, TaskCategoryKinds.Checklist)]
    [InlineData("Offboarding", null, TaskCategoryKinds.Checklist)]
    [InlineData("task", null, TaskCategoryKinds.Task)]
    [InlineData("Chore", null, TaskCategoryKinds.Task)]
    [InlineData("Spike", null, TaskCategoryKinds.Task)]
    [InlineData("anything", TaskCategorySystemKeys.Meeting, TaskCategoryKinds.Meeting)]
    public void Infer_maps_a_name_to_its_kind_and_anything_else_to_task(string name, string? systemKey, string expected)
        => Assert.Equal(expected, TaskCategoryKinds.Infer(name, systemKey));

    [Fact]
    public void All_lists_the_seven_kinds_in_display_order()
        => Assert.Equal(["task", "bug", "story", "feature", "testing", "meeting", "checklist"], TaskCategoryKinds.All);

    [Fact]
    public void A_new_category_defaults_to_task()
        => Assert.Equal(TaskCategoryKinds.Task, new TaskCategory().Kind);
}
