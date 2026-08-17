using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class SprintConfigurationTests
{
    [Fact]
    public void Sprint_DefaultsStatusToFuture()
    {
        var sprint = new Sprint
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), ProjectId = Guid.NewGuid(), ObjectiveId = Guid.NewGuid(),
            Name = "Sprint 1", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 14),
            CreatedAt = DateTimeOffset.UtcNow
        };

        Assert.Equal(SprintStatuses.Future, sprint.Status);
    }
}
