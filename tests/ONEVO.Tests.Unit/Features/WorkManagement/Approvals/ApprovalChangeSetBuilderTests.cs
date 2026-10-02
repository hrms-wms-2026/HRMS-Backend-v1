using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public class ApprovalChangeSetBuilderTests
{
    private static readonly IReadOnlyDictionary<string, string?> NoCurrent = new Dictionary<string, string?>();

    [Fact]
    public void Task_edit_keeps_stored_pascal_case_keys_and_flags_changes()
    {
        var current = new Dictionary<string, string?> { ["title"] = "Old", ["priority"] = "High", ["dueDate"] = "2026-10-01" };

        var fields = ApprovalChangeSetBuilder.Build(WorkActionTypes.TaskEdit,
            """{"Title":"New","Priority":"High","DueDate":"2026-10-05"}""", null, current, editable: true);

        var title = fields.Single(f => f.Label == "Title");
        title.Key.Should().Be("Title");
        title.Current.Should().Be("Old");
        title.Requested.Should().Be("New");
        title.Changed.Should().BeTrue();
        title.Editable.Should().BeTrue();
        fields.Single(f => f.Label == "Priority").Changed.Should().BeFalse();
        fields.Single(f => f.Label == "Due date").Requested.Should().Be("2026-10-05");
        fields.Single(f => f.Label == "Description").Editable.Should().BeTrue("long text fields are editable like any other field on an editable action");
        fields.Should().Contain(f => f.Label == "Progress %");
    }

    [Fact]
    public void Absent_property_falls_back_to_the_camel_name_with_no_requested_value()
    {
        var fields = ApprovalChangeSetBuilder.Build(WorkActionTypes.TaskEdit, """{"Title":"New"}""", null, NoCurrent, editable: true);

        var estimate = fields.Single(f => f.Label == "Estimated hours");
        estimate.Key.Should().Be("estimatedHours");
        estimate.Requested.Should().BeNull();
    }

    [Fact]
    public void Module_edit_applied_values_only_on_fields_present_in_applied_payload()
    {
        var fields = ApprovalChangeSetBuilder.Build(WorkActionTypes.ModuleEdit,
            """{"title":"A","allocatedHours":40,"startDate":"2026-10-01"}""",
            """{"allocatedHours":30.5}""",
            new Dictionary<string, string?> { ["title"] = "A", ["allocatedHours"] = "20" }, editable: false);

        fields.Single(f => f.Key == "allocatedHours").Applied.Should().Be("30.5");
        fields.Single(f => f.Key == "allocatedHours").Requested.Should().Be("40");
        fields.Single(f => f.Key == "title").Applied.Should().BeNull();
        fields.Single(f => f.Key == "title").Changed.Should().BeFalse();
        fields.Should().OnlyContain(f => !f.Editable);
    }

    [Fact]
    public void Allocation_has_a_single_hours_field()
    {
        var fields = ApprovalChangeSetBuilder.Build(WorkActionTypes.ModuleAllocationExtend,
            """{"requestedAdditionalHours":35,"reason":"scope"}""", null, NoCurrent, editable: true);

        var field = fields.Should().ContainSingle().Subject;
        field.Key.Should().Be("requestedAdditionalHours");
        field.Requested.Should().Be("35");
        field.Kind.Should().Be("hours");
        field.Changed.Should().BeTrue();
        field.Editable.Should().BeTrue();
    }

    [Fact]
    public void Task_create_marks_every_requested_field_changed()
    {
        var fields = ApprovalChangeSetBuilder.Build(WorkActionTypes.TaskCreate,
            """{"Title":"New","Priority":null}""", null, NoCurrent, editable: true);

        fields.Single(f => f.Key == "Title").Changed.Should().BeTrue();
        fields.Single(f => f.Key == "Priority").Changed.Should().BeFalse();
        fields.Should().NotContain(f => f.Label == "Progress %");
    }

    [Fact]
    public void Transfer_uses_display_overrides_and_is_never_editable()
    {
        var fields = ApprovalChangeSetBuilder.Build(WorkActionTypes.ModuleTransfer,
            """{"newHeadEmployeeId":"8f2c3a1e-0000-0000-0000-000000000001"}""", null,
            new Dictionary<string, string?> { ["newHeadEmployeeId"] = "Anu" }, editable: true,
            requestedOverrides: new Dictionary<string, string?> { ["newHeadEmployeeId"] = "Bala" });

        var field = fields.Should().ContainSingle().Subject;
        field.Label.Should().Be("New owner");
        field.Current.Should().Be("Anu");
        field.Requested.Should().Be("Bala");
        field.Editable.Should().BeFalse();
    }

    [Fact]
    public void Sprint_start_is_never_editable()
        => ApprovalChangeSetBuilder.Build(WorkActionTypes.SprintStart,
                """{"startDate":"2026-10-01","endDate":"2026-10-14","goal":null}""", null, NoCurrent, editable: true)
            .Should().OnlyContain(f => !f.Editable);

    [Fact]
    public void Unknown_action_has_no_fields()
        => ApprovalChangeSetBuilder.Build(WorkActionTypes.TaskDelete, "{}", null, NoCurrent, editable: true).Should().BeEmpty();

    [Fact]
    public void Editable_false_disables_every_field()
        => ApprovalChangeSetBuilder.Build(WorkActionTypes.TaskEdit, """{"Title":"x"}""", null, NoCurrent, editable: false)
            .Should().OnlyContain(f => !f.Editable);
}
