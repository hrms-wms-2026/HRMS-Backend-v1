using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class SprintActivityLogFactoryTests
{
    [Fact]
    public void Create_FillsIdentityStatusAndSerializesDetails()
    {
        var tenantId = Guid.NewGuid();
        var sprintId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var log = SprintActivityLogFactory.Create(tenantId, sprintId, employeeId, SprintActivityActions.Started,
            SprintStatuses.Draft, SprintStatuses.Active, new { taskIds = new[] { taskId } });

        Assert.NotEqual(Guid.Empty, log.Id);
        Assert.Equal(tenantId, log.TenantId);
        Assert.Equal(sprintId, log.SprintId);
        Assert.Equal(employeeId, log.EmployeeId);
        Assert.Equal("started", log.Action);
        Assert.Equal("draft", log.FromStatus);
        Assert.Equal("active", log.ToStatus);
        Assert.Contains(taskId.ToString(), log.DetailsJson);
    }

    [Fact]
    public void Create_NoDetails_LeavesDetailsJsonNull()
    {
        var log = SprintActivityLogFactory.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SprintActivityActions.Created);
        Assert.Null(log.DetailsJson);
        Assert.Null(log.FromStatus);
    }
}
