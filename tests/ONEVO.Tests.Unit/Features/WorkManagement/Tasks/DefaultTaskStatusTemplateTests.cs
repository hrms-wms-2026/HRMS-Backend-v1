using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class DefaultTaskStatusTemplateTests
{
    [Fact]
    public void BuildRows_AssignsExactlyOneNotStartedOneDoneAndAtLeastOneActive()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var createdById = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var rows = DefaultTaskStatusTemplate.BuildRows(tenantId, projectId, null, createdById, now);

        Assert.Single(rows, r => r.Category == TaskStatusCategories.NotStarted);
        Assert.True(rows.Count(r => r.Category == TaskStatusCategories.Active) >= 1);
        Assert.Single(rows, r => r.Category == TaskStatusCategories.Done);
        Assert.Single(rows, r => r.MarksTaskComplete);
        Assert.All(rows, r => Assert.Matches("^#[0-9A-Fa-f]{6}$", r.Color));
    }

    [Fact]
    public void BuildRows_DoneRowMarksTaskCompleteMatchesCategory()
    {
        var rows = DefaultTaskStatusTemplate.BuildRows(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), DateTimeOffset.UtcNow);

        var done = rows.Single(r => r.Category == TaskStatusCategories.Done);
        Assert.True(done.MarksTaskComplete);
        Assert.Equal(TaskStatusVisibilities.Private, done.Visibility);
    }
}
