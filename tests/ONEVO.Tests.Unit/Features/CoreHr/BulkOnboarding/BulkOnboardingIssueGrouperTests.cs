using ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Models;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.BulkOnboarding;

public sealed class BulkOnboardingIssueGrouperTests
{
    [Fact]
    public void Group_SameMissingDepartment_BecomesOneIssue()
    {
        var errors = new List<(int, RowValidationError)>
        {
            (2, new RowValidationError(BulkOnboardingIssueTypes.DepartmentNotFound, "department", "Department 'Human Resorces' was not found.", "Human Resorces")),
            (5, new RowValidationError(BulkOnboardingIssueTypes.DepartmentNotFound, "department", "Department 'Human Resorces' was not found.", "Human Resorces")),
            (9, new RowValidationError(BulkOnboardingIssueTypes.DepartmentNotFound, "department", "Department 'Human Resorces' was not found.", "Human Resorces")),
        };

        var catalog = new Dictionary<string, IReadOnlyList<(string Id, string Label)>>
        {
            ["department"] = [("dept-1", "Human Resources")]
        };

        var issues = BulkOnboardingIssueGrouper.Group(errors, catalog, canManageOrg: true);

        var issue = Assert.Single(issues);
        Assert.Equal("department_not_found:Human Resorces", issue.IssueKey);
        Assert.Equal(3, issue.AffectedRowCount);
        Assert.Equal([2, 5, 9], issue.AffectedRowNumbers);
        Assert.Contains(BulkOnboardingIssueTypes.Actions.CreateDepartment, issue.AllowedActions);
        Assert.Contains(BulkOnboardingIssueTypes.Actions.MapExisting, issue.AllowedActions);
        var suggestion = Assert.Single(issue.Suggestions);
        Assert.Equal("Human Resources", suggestion.Label);
    }

    [Fact]
    public void Group_WithoutOrgManage_OmitsCreateActions()
    {
        var errors = new List<(int, RowValidationError)>
        {
            (1, new RowValidationError(BulkOnboardingIssueTypes.PositionNotFound, "position", "Position 'X' was not found.", "X")),
        };

        var issues = BulkOnboardingIssueGrouper.Group(errors, new Dictionary<string, IReadOnlyList<(string, string)>>(), canManageOrg: false);

        var issue = Assert.Single(issues);
        Assert.DoesNotContain(BulkOnboardingIssueTypes.Actions.CreatePosition, issue.AllowedActions);
        Assert.Contains(BulkOnboardingIssueTypes.Actions.MapExisting, issue.AllowedActions);
    }

    [Fact]
    public void Group_DuplicateEmail_IsRowEditOnly()
    {
        var errors = new List<(int, RowValidationError)>
        {
            (3, new RowValidationError(BulkOnboardingIssueTypes.DuplicateWorkEmail, "workEmail", "Duplicate", "a@x.com")),
        };

        var issues = BulkOnboardingIssueGrouper.Group(errors, new Dictionary<string, IReadOnlyList<(string, string)>>(), canManageOrg: true);

        var issue = Assert.Single(issues);
        Assert.Equal([BulkOnboardingIssueTypes.Actions.EditImportedValue], issue.AllowedActions);
        Assert.Empty(issue.Suggestions);
    }

    [Fact]
    public void AllowedActions_PositionCapacity_IncludesIncreaseWhenCanManage()
    {
        var withManage = BulkOnboardingIssueGrouper.AllowedActionsFor(
            BulkOnboardingIssueTypes.PositionCapacityExceeded, canManageOrg: true);
        Assert.Contains(BulkOnboardingIssueTypes.Actions.IncreaseCapacity, withManage);
        Assert.Contains(BulkOnboardingIssueTypes.Actions.MapExisting, withManage);

        var without = BulkOnboardingIssueGrouper.AllowedActionsFor(
            BulkOnboardingIssueTypes.PositionCapacityExceeded, canManageOrg: false);
        Assert.DoesNotContain(BulkOnboardingIssueTypes.Actions.IncreaseCapacity, without);
        Assert.Contains(BulkOnboardingIssueTypes.Actions.MapExisting, without);
    }

    [Fact]
    public void Group_PositionCapacityExceeded_GroupsByPositionName()
    {
        var errors = new List<(int, RowValidationError)>
        {
            (1, new RowValidationError(BulkOnboardingIssueTypes.PositionCapacityExceeded, "position", "msg", "Project Manager")),
            (2, new RowValidationError(BulkOnboardingIssueTypes.PositionCapacityExceeded, "position", "msg", "Project Manager")),
            (3, new RowValidationError(BulkOnboardingIssueTypes.PositionCapacityExceeded, "position", "msg", "Project Manager")),
            (4, new RowValidationError(BulkOnboardingIssueTypes.PositionCapacityExceeded, "position", "msg", "Project Manager")),
        };

        var issues = BulkOnboardingIssueGrouper.Group(errors, new Dictionary<string, IReadOnlyList<(string, string)>>(), canManageOrg: true);
        var issue = Assert.Single(issues);
        Assert.Equal("position_capacity_exceeded:Project Manager", issue.IssueKey);
        Assert.Equal(4, issue.AffectedRowCount);
        Assert.Contains(BulkOnboardingIssueTypes.Actions.IncreaseCapacity, issue.AllowedActions);
    }
}
