using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskCreationRequestConfigurationTests
{
    [Fact]
    public void TaskCreationRequest_DefaultsToPendingStatus()
    {
        var request = new TaskCreationRequest
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), ObjectiveId = Guid.NewGuid(),
            RequestedByEmployeeId = Guid.NewGuid(), PayloadJson = "{}", CreatedById = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        Assert.Equal(TaskCreationRequestStatuses.Pending, request.Status);
        Assert.Null(request.DecidedByEmployeeId);
        Assert.Null(request.CreatedTaskId);
    }
}
