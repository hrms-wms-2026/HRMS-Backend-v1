using FluentAssertions;
using ONEVO.Application.Features.CoreHr.Onboarding.Models;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public class ChecklistTaskJsonContractTests
{
    [Fact]
    public void Parse_OffsetMode_ParsesValidTemplateTask()
    {
        var userId = Guid.NewGuid();
        var json = $"[{{\"title\":\"Submit ID\",\"ownerType\":\"custom_user\",\"assignedToId\":\"{userId}\",\"dueOffsetDays\":3,\"sequence\":1,\"isRequired\":true}}]";

        var result = ChecklistTaskJsonContract.Parse(json, ChecklistTaskDueRuleMode.OffsetDays);

        result.Should().ContainSingle();
        result[0].Title.Should().Be("Submit ID");
        result[0].OwnerType.Should().Be("custom_user");
        result[0].AssignedToId.Should().Be(userId);
        result[0].DueOffsetDays.Should().Be(3);
        result[0].DueDate.Should().BeNull();
        result[0].Sequence.Should().Be(1);
        result[0].IsRequired.Should().BeTrue();
    }

    [Fact]
    public void Parse_AbsoluteDateMode_ParsesValidEditedTask()
    {
        var userId = Guid.NewGuid();
        var json = $"[{{\"title\":\"Sign NDA\",\"ownerType\":\"custom_user\",\"assignedToId\":\"{userId}\",\"dueDate\":\"2026-09-01\",\"isRequired\":false}}]";

        var result = ChecklistTaskJsonContract.Parse(json, ChecklistTaskDueRuleMode.AbsoluteDate);

        result[0].DueDate.Should().Be(new DateOnly(2026, 9, 1));
        result[0].DueOffsetDays.Should().BeNull();
        result[0].IsRequired.Should().BeFalse();
    }

    [Fact]
    public void Parse_EmployeeOwnerType_AllowsNullAssignedToId()
    {
        var json = "[{\"title\":\"Complete profile\",\"ownerType\":\"employee\",\"dueOffsetDays\":1,\"isRequired\":true}]";

        var result = ChecklistTaskJsonContract.Parse(json, ChecklistTaskDueRuleMode.OffsetDays);

        result[0].AssignedToId.Should().BeNull();
    }

    [Fact]
    public void Parse_EmployeeOwnerType_RejectsExplicitAssignedToId()
    {
        var json = $"[{{\"title\":\"x\",\"ownerType\":\"employee\",\"assignedToId\":\"{Guid.NewGuid()}\",\"dueOffsetDays\":1,\"isRequired\":true}}]";

        var act = () => ChecklistTaskJsonContract.Parse(json, ChecklistTaskDueRuleMode.OffsetDays);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("hr")]
    [InlineData("it")]
    [InlineData("custom_user")]
    public void Parse_NonEmployeeOwnerType_RequiresAssignedToId(string ownerType)
    {
        var json = $"[{{\"title\":\"x\",\"ownerType\":\"{ownerType}\",\"dueOffsetDays\":1,\"isRequired\":true}}]";

        var act = () => ChecklistTaskJsonContract.Parse(json, ChecklistTaskDueRuleMode.OffsetDays);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_MissingIsRequired_Throws()
    {
        var json = "[{\"title\":\"x\",\"ownerType\":\"employee\",\"dueOffsetDays\":1}]";

        var act = () => ChecklistTaskJsonContract.Parse(json, ChecklistTaskDueRuleMode.OffsetDays);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_UnknownOwnerType_Throws()
    {
        var json = $"[{{\"title\":\"x\",\"ownerType\":\"finance\",\"assignedToId\":\"{Guid.NewGuid()}\",\"dueOffsetDays\":1,\"isRequired\":true}}]";

        var act = () => ChecklistTaskJsonContract.Parse(json, ChecklistTaskDueRuleMode.OffsetDays);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_NegativeDueOffsetDays_Throws()
    {
        var json = "[{\"title\":\"x\",\"ownerType\":\"employee\",\"dueOffsetDays\":-1,\"isRequired\":true}]";

        var act = () => ChecklistTaskJsonContract.Parse(json, ChecklistTaskDueRuleMode.OffsetDays);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SerializeTemplateTasks_RoundTrips_ThroughParse()
    {
        var defs = new List<ChecklistTaskDefinition>
        {
            new("Submit ID", ChecklistTaskOwnerTypes.Employee, null, 2, null, 1, true),
            new("Assign badge", ChecklistTaskOwnerTypes.CustomUser, Guid.NewGuid(), 5, null, 2, false),
        };

        var json = ChecklistTaskJsonContract.SerializeTemplateTasks(defs);
        var roundTripped = ChecklistTaskJsonContract.Parse(json, ChecklistTaskDueRuleMode.OffsetDays);

        roundTripped.Should().BeEquivalentTo(defs);
    }

    [Fact]
    public void ToEmployeeChecklistTasks_OffsetMode_ResolvesEmployeeOwnerToNewHireUserId_AndOffsetsFromAnchor()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var newHireUserId = Guid.NewGuid();
        var anchor = new DateOnly(2026, 10, 1);
        var defs = new List<ChecklistTaskDefinition> { new("Complete profile", ChecklistTaskOwnerTypes.Employee, null, 3, null, 1, true) };

        var tasks = ChecklistTaskJsonContract.ToEmployeeChecklistTasks(
            defs, tenantId, employeeId, templateId, "onboarding", newHireUserId, anchor, ChecklistTaskDueRuleMode.OffsetDays);

        tasks.Should().ContainSingle();
        tasks[0].AssignedToId.Should().Be(newHireUserId);
        tasks[0].DueDate.Should().Be(new DateOnly(2026, 10, 4));
        tasks[0].TenantId.Should().Be(tenantId);
        tasks[0].EmployeeId.Should().Be(employeeId);
        tasks[0].TemplateId.Should().Be(templateId);
        tasks[0].IsRequired.Should().BeTrue();
        tasks[0].Status.Should().Be("pending");
    }

    [Fact]
    public void ToEmployeeChecklistTasks_AbsoluteDateMode_UsesConcreteDueDate_AndConcreteAssignedToId()
    {
        var assignedTo = Guid.NewGuid();
        var defs = new List<ChecklistTaskDefinition> { new("Sign NDA", ChecklistTaskOwnerTypes.CustomUser, assignedTo, null, new DateOnly(2026, 11, 1), null, false) };

        var tasks = ChecklistTaskJsonContract.ToEmployeeChecklistTasks(
            defs, Guid.NewGuid(), Guid.NewGuid(), null, "offboarding", Guid.NewGuid(), new DateOnly(2026, 1, 1), ChecklistTaskDueRuleMode.AbsoluteDate);

        tasks[0].AssignedToId.Should().Be(assignedTo);
        tasks[0].DueDate.Should().Be(new DateOnly(2026, 11, 1));
    }

    [Fact]
    public void Parse_OffboardingFields_DefaultToFalseAndNull_WhenAbsent()
    {
        var json = "[{\"title\":\"Return laptop\",\"ownerType\":\"employee\",\"dueOffsetDays\":1,\"isRequired\":true}]";

        var result = ChecklistTaskJsonContract.Parse(json, ChecklistTaskDueRuleMode.OffsetDays);

        result[0].IsBypassable.Should().BeFalse();
        result[0].BypassPenaltyDescription.Should().BeNull();
        result[0].Category.Should().BeNull();
    }

    [Fact]
    public void Parse_OffboardingFields_ParsesWhenPresent()
    {
        var json = "[{\"title\":\"Return laptop\",\"ownerType\":\"employee\",\"dueOffsetDays\":1,\"isRequired\":true," +
                    "\"isBypassable\":true,\"bypassPenaltyDescription\":\"Deduct from final settlement\",\"category\":\"asset_return\"}]";

        var result = ChecklistTaskJsonContract.Parse(json, ChecklistTaskDueRuleMode.OffsetDays);

        result[0].IsBypassable.Should().BeTrue();
        result[0].BypassPenaltyDescription.Should().Be("Deduct from final settlement");
        result[0].Category.Should().Be("asset_return");
    }

    [Fact]
    public void ToEmployeeChecklistTasks_CopiesOffboardingFieldsOntoTheInstantiatedTask()
    {
        var defs = new List<ChecklistTaskDefinition>
        {
            new("Return laptop", ChecklistTaskOwnerTypes.Employee, null, 1, null, 1, true, true, "None", "asset_return"),
        };

        var tasks = ChecklistTaskJsonContract.ToEmployeeChecklistTasks(
            defs, Guid.NewGuid(), Guid.NewGuid(), null, "offboarding", Guid.NewGuid(), new DateOnly(2026, 1, 1), ChecklistTaskDueRuleMode.OffsetDays);

        tasks[0].IsBypassable.Should().BeTrue();
        tasks[0].BypassPenaltyDescription.Should().Be("None");
        tasks[0].Category.Should().Be("asset_return");
    }
}
