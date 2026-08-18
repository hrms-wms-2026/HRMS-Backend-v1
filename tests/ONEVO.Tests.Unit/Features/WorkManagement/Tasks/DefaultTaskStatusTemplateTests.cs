using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class DefaultTaskStatusTemplateTests
{
    [Fact]
    public void BuildRows_Returns4RowsInDisplayOrderWithCorrectVisibilityAndCompletionFlag()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var objectiveId = Guid.NewGuid();
        var createdById = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var rows = DefaultTaskStatusTemplate.BuildRows(tenantId, projectId, objectiveId, createdById, now);

        Assert.Equal(4, rows.Count);
        Assert.Equal(4, rows.Select(r => r.Id).Distinct().Count());
        Assert.All(rows, r => Assert.Equal(tenantId, r.TenantId));
        Assert.All(rows, r => Assert.Equal(projectId, r.ProjectId));
        Assert.All(rows, r => Assert.Equal(objectiveId, r.ObjectiveId));

        var ordered = rows.OrderBy(r => r.DisplayOrder).ToList();
        Assert.Equal(new[] { "To Do", "In Process", "Review", "Done" }, ordered.Select(r => r.Name));
        Assert.Equal(new[] { 0, 1, 2, 3 }, ordered.Select(r => r.DisplayOrder));
        Assert.Equal(
            new[] { TaskStatusVisibilities.Public, TaskStatusVisibilities.Public, TaskStatusVisibilities.Public, TaskStatusVisibilities.Private },
            ordered.Select(r => r.Visibility));
        Assert.Equal(new[] { false, false, false, true }, ordered.Select(r => r.MarksTaskComplete));
    }

    [Fact]
    public void BuildRows_NullObjectiveId_ProducesProjectLevelTemplateRows()
    {
        var rows = DefaultTaskStatusTemplate.BuildRows(Guid.NewGuid(), Guid.NewGuid(), objectiveId: null, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.All(rows, r => Assert.Null(r.ObjectiveId));
    }
}
