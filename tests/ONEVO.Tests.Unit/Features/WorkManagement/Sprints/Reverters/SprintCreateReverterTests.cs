using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Reverters;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints.Reverters;

public class SprintCreateReverterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly Mock<ISprintWriteService> _writes = new();
    private readonly Mock<ISprintRepository> _sprints = new();

    private SprintCreateReverter Build() => new(_writes.Object, _sprints.Object);

    [Fact]
    public async Task RevertAsync_DeletesTheCreatedSprintAndNullsTargetId()
    {
        var sprintId = Guid.NewGuid();
        var sprint = new Sprint { Id = sprintId, TenantId = TenantId };
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = sprintId, ActionType = WorkActionTypes.SprintCreate };
        _sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, sprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        _writes.Verify(x => x.ApplyDeleteAsync(TenantId, sprint, It.IsAny<CancellationToken>()), Times.Once);
        request.TargetId.Should().BeNull();
    }

    [Fact]
    public async Task RevertAsync_SprintAlreadyGone_ReturnsStale()
    {
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = Guid.NewGuid(), ActionType = WorkActionTypes.SprintCreate };
        _sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, request.TargetId!.Value, It.IsAny<CancellationToken>())).ReturnsAsync((Sprint?)null);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Stale);
    }
}
