using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.DeleteSprint;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;
using K = ONEVO.Tests.Unit.Features.WorkManagement.Sprints.SprintEngineKit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class DeleteSprintCommandHandlerTests
{
    [Fact]
    public async Task Creator_DeletesDirect_EvenIfNotAtOrAbovePosition()
    {
        var kit = new K();
        var sprint = kit.GivenSprint(SprintStatuses.Complete, creator: K.Member, position: kit.B.Id);
        var task = new WorkTask { Id = Guid.NewGuid(), SprintId = sprint.Id };
        kit.W.Tasks.Setup(x => x.GetBySprintIdAsync(K.TenantId, sprint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkTask> { task });
        kit.W.Tasks.Setup(x => x.GetTrackedByIdForTenantAsync(K.TenantId, task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        kit.CallAs(K.Member);

        var result = await kit.Delete().Handle(new DeleteSprintCommand(sprint.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Sprint.Should().BeNull();
        result.Value.ApprovalRequestId.Should().BeNull();
        task.SprintId.Should().BeNull();
        kit.W.Sprints.Verify(x => x.Remove(sprint), Times.Once);
        kit.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task ParentOfCreatorPosition_DeletesDirect()
    {
        var kit = new K();
        var sprint = kit.GivenSprint(SprintStatuses.Achieved, creator: K.Member, position: kit.B.Id);
        kit.CallAs(K.OwnerA);

        var result = await kit.Delete().Handle(new DeleteSprintCommand(sprint.Id), CancellationToken.None);

        result.Value!.ApprovalRequestId.Should().BeNull();
        kit.W.Sprints.Verify(x => x.Remove(sprint), Times.Once);
    }

    [Fact]
    public async Task OtherMember_Pending()
    {
        var kit = new K();
        var sprint = kit.GivenSprint(SprintStatuses.Complete, creator: K.OwnerB, position: kit.B.Id);
        kit.CallAs(K.Member);

        var result = await kit.Delete().Handle(new DeleteSprintCommand(sprint.Id), CancellationToken.None);

        result.Value!.ApprovalRequestId.Should().NotBeNull();
        kit.Added.Single().ActionType.Should().Be(WorkActionTypes.SprintDelete);
        kit.Added.Single().ApproverEmployeeId.Should().Be(K.OwnerB);
        kit.W.Sprints.Verify(x => x.Remove(It.IsAny<Sprint>()), Times.Never);
    }

    [Fact]
    public async Task ActiveSprint_Conflict409_EngineNeverCalled()
    {
        var kit = new K();
        var sprint = kit.GivenSprint(SprintStatuses.Active, creator: K.Member, position: kit.B.Id);
        kit.CallAs(K.Member);

        var result = await kit.Delete().Handle(new DeleteSprintCommand(sprint.Id), CancellationToken.None);

        result.StatusCode.Should().Be(409);
        result.Error.Should().Be("Only a completed or achieved sprint can be deleted.");
        kit.Added.Should().BeEmpty();
        kit.W.Sprints.Verify(x => x.Remove(It.IsAny<Sprint>()), Times.Never);
    }

    [Fact]
    public async Task DraftSprint_Conflict409()
    {
        var kit = new K();
        var sprint = kit.GivenSprint(SprintStatuses.Draft, creator: K.OwnerA, position: kit.A.Id);
        kit.CallAs(K.OwnerA);

        var result = await kit.Delete().Handle(new DeleteSprintCommand(sprint.Id), CancellationToken.None);

        result.StatusCode.Should().Be(409);
    }
}
