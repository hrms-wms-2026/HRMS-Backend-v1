using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskEditRequestConfigurationTests
{
    [Fact]
    public void TaskEditRequest_DefaultsStatusToPending()
    {
        var request = new TaskEditRequest { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), TaskId = Guid.NewGuid(), RequestedByEmployeeId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };

        Assert.Equal(TaskEditRequestStatuses.Pending, request.Status);
    }
}
