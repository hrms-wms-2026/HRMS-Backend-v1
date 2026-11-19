using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.AchieveSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CreateSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.EditSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.StartSprint;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;
using K = ONEVO.Tests.Unit.Features.WorkManagement.Sprints.SprintEngineKit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class SprintActionsThroughEngineTests
{
    private static CreateSprintCommand CreateCommand() => new(K.ProjectId, "Sprint 1", null, Array.Empty<Guid>());

    [Fact]
    public async Task Create_OwnerOfModule_PositionIsHighestOwned_Direct()
    {
        var kit = new K();
        kit.CallAs(K.OwnerA);

        var result = await kit.Create().Handle(CreateCommand(), CancellationToken.None);

        result.Value!.Sprint.Should().NotBeNull();
        result.Value.ApprovalRequestId.Should().BeNull();
        kit.W.Sprints.Verify(x => x.AddAsync(It.Is<Sprint>(s => s.CreatorPositionObjectiveId == kit.A.Id), It.IsAny<CancellationToken>()), Times.Once);
        kit.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_PlainMember_PositionIsHighestMemberModule_Pending()
    {
        var kit = new K();
        kit.CallAs(K.Member);

        var result = await kit.Create().Handle(CreateCommand(), CancellationToken.None);

        result.Value!.ApprovalRequestId.Should().NotBeNull();
        var request = kit.Added.Single();
        request.ActionType.Should().Be(WorkActionTypes.SprintCreate);
        request.PositionObjectiveId.Should().Be(kit.B.Id);
        request.ApproverEmployeeId.Should().Be(K.OwnerB);
        request.TargetId.Should().BeNull();
        kit.W.Sprints.Verify(x => x.AddAsync(It.IsAny<Sprint>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_NoOwnershipNoMembership_PositionIsRoot()
    {
        var kit = new K();
        kit.CallAs(K.Outsider);

        await kit.Create().Handle(CreateCommand(), CancellationToken.None);

        var request = kit.Added.Single();
        request.PositionObjectiveId.Should().Be(kit.W.Root.Id);
        request.ApproverEmployeeId.Should().Be(kit.W.RootOwner);
    }

    [Fact]
    public async Task Edit_ByParentOfPosition_Direct_NotifiesHolderAndCreator()
    {
        var kit = new K();
        var sprint = kit.GivenSprint(SprintStatuses.Active, creator: K.Member, position: kit.B.Id);
        kit.CallAs(K.OwnerA);

        var result = await kit.Edit().Handle(new EditSprintCommand(sprint.Id, "Renamed", null, null, null), CancellationToken.None);

        result.Value!.Sprint!.Name.Should().Be("Renamed");
        var e = kit.W.Notified.Single(n => n.Kind == WorkNotificationKinds.Direct);
        e.ActionType.Should().Be(WorkActionTypes.SprintEdit);
        e.RecipientEmployeeIds.Should().BeEquivalentTo(new[] { K.OwnerB, K.Member });
    }

    [Fact]
    public async Task Edit_ByOtherMember_Pending()
    {
        var kit = new K();
        var sprint = kit.GivenSprint(SprintStatuses.Active, creator: K.OwnerB, position: kit.B.Id);
        kit.CallAs(K.Member);

        var result = await kit.Edit().Handle(new EditSprintCommand(sprint.Id, "Renamed", null, null, null), CancellationToken.None);

        result.Value!.ApprovalRequestId.Should().NotBeNull();
        sprint.Name.Should().Be("S1");
        var request = kit.Added.Single();
        request.ApproverEmployeeId.Should().Be(K.OwnerB);
        request.TargetId.Should().Be(sprint.Id);
        request.TargetType.Should().Be(WorkTargetTypes.Sprint);
        request.PositionObjectiveId.Should().Be(kit.B.Id);
    }

    [Fact]
    public async Task Start_Complete_Achieve_ByOtherMember_Pending_WithTheirPayload()
    {
        var start = new K();
        var draft = start.GivenSprint(SprintStatuses.Draft, creator: K.OwnerB, position: start.B.Id);
        start.CallAs(K.Member);
        await start.Start().Handle(new StartSprintCommand(draft.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 14), null), CancellationToken.None);
        start.Added.Single().ActionType.Should().Be(WorkActionTypes.SprintStart);
        start.Added.Single().PayloadJson.Should().Contain("\"startDate\":\"2026-10-01\"").And.Contain("\"endDate\":\"2026-10-14\"");
        draft.Status.Should().Be(SprintStatuses.Draft);

        var complete = new K();
        var active = complete.GivenSprint(SprintStatuses.Active, creator: K.OwnerB, position: complete.B.Id);
        complete.CallAs(K.Member);
        await complete.Complete().Handle(new CompleteSprintCommand(active.Id, "backlog", null), CancellationToken.None);
        complete.Added.Single().ActionType.Should().Be(WorkActionTypes.SprintComplete);
        complete.Added.Single().PayloadJson.Should().Be("{\"disposition\":\"backlog\",\"targetSprintId\":null}");
        active.Status.Should().Be(SprintStatuses.Active);

        var achieve = new K();
        var done = achieve.GivenSprint(SprintStatuses.Complete, creator: K.OwnerB, position: achieve.B.Id);
        achieve.CallAs(K.Member);
        await achieve.Achieve().Handle(new AchieveSprintCommand(done.Id), CancellationToken.None);
        achieve.Added.Single().ActionType.Should().Be(WorkActionTypes.SprintAchieve);
        done.Status.Should().Be(SprintStatuses.Complete);
    }

    [Fact]
    public async Task NonProjectMember_Forbidden_EngineNeverCalled()
    {
        var kit = new K();
        var sprint = kit.GivenSprint(SprintStatuses.Active, creator: K.OwnerB, position: kit.B.Id);
        kit.CallAs(K.NonMember);

        var result = await kit.Edit().Handle(new EditSprintCommand(sprint.Id, "Renamed", null, null, null), CancellationToken.None);

        result.StatusCode.Should().Be(403);
        result.Error.Should().Be("Only project members can change sprints.");
        kit.Added.Should().BeEmpty();
        kit.W.Hierarchy.Verify(x => x.LoadTreeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
