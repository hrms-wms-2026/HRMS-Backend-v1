using System.Text.Json;
using FluentAssertions;
using MediatR;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.WorkManagement.Approvals.Commands.DecideWorkApprovalRequest;
using ONEVO.Application.Features.WorkManagement.Approvals.Commands.RevertWorkApprovalRequest;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Inbox;
using ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.AcceptObjectiveInvitation;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.CancelObjectiveInvitation;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.RejectObjectiveInvitation;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Tests.Unit.Features.WorkManagement.Approvals;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Inbox;

public class WorkInboxSourceProviderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Approver = Guid.NewGuid();
    private static readonly Guid NewHolder = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly Project _a = new() { Id = Guid.NewGuid(), Name = "Apollo", LeadId = Guid.NewGuid() };
    private readonly Project _b = new() { Id = Guid.NewGuid(), Name = "Borealis", LeadId = Guid.NewGuid() };
    private readonly Project _c = new() { Id = Guid.NewGuid(), Name = "Cetus", LeadId = Guid.NewGuid() };
    private readonly Dictionary<Guid, (Guid Root, Guid Module, Guid RootOwner)> _trees = new();

    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<IProjectMemberInvitationRepository> _invitations = new();
    private readonly Mock<IWorkApprovalRequestRepository> _requests = new();
    private readonly Mock<IWorkHierarchyService> _hierarchy = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IMonitorAlertRepository> _alerts = new();
    private readonly Mock<IProjectMonitorSnapshotLoader> _snapshots = new();
    private readonly Mock<ISender> _sender = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly ApprovalCommentHandlersTests.InMemoryApprovalCommentRepository _comments = new();
    private readonly List<Project> _memberOf = new();
    private readonly List<WorkApprovalRequest> _rows = new();
    private readonly List<ProjectMemberInvitation> _invites = new();
    private readonly List<MonitorAlert> _alertRows = new();

    public WorkInboxSourceProviderTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        foreach (var p in new[] { _a, _b, _c })
        {
            var root = Guid.NewGuid();
            var module = Guid.NewGuid();
            var rootOwner = Guid.NewGuid();
            _trees[p.Id] = (root, module, rootOwner);
            _hierarchy.Setup(h => h.LoadTreeAsync(TenantId, p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => new ProjectModuleTree(
            [
                new Objective { Id = root, OwnerId = rootOwner, IsDefault = true, Title = "Root" },
                new Objective { Id = module, ParentObjectiveId = root, OwnerId = Approver, Title = $"{p.Name} Payments", IsAchieved = _achieved.Contains(module) }
            ]));
            _snapshots.Setup(s => s.LoadAsync(TenantId, p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new ProjectMonitorSnapshot(
                p.Id, WorkCalendar.Default,
                [new MonitorModule(module, root, null, "Payments", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 30), 10m, 0m, false, 1)], [], []));
            _projects.Setup(r => r.GetByIdForTenantAsync(TenantId, p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p);
            _requests.Setup(r => r.ListByProjectAsync(TenantId, p.Id, null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _rows.Where(r => r.ProjectId == p.Id).ToList());
            _invitations.Setup(r => r.ListForProjectAndEmployeeAsync(TenantId, p.Id, Me, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _invites.Where(i => i.ProjectId == p.Id).ToList());
            _alerts.Setup(r => r.ListOpenOrResolvedSinceAsync(TenantId, p.Id, Now.AddDays(-30), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _alertRows.Where(a => a.ProjectId == p.Id).ToList());
        }
        _hierarchy.Setup(h => h.FindActiveHolderAsync(TenantId, It.IsAny<ProjectModuleTree>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, ProjectModuleTree tree, Guid position, Guid _, CancellationToken _) => tree.Get(position)?.OwnerId);
        _projects.Setup(r => r.ListForMemberAsync(TenantId, Me, 0, int.MaxValue, null, "asc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (_memberOf.ToList(), _memberOf.Count));
        _projects.Setup(r => r.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, CancellationToken _) => new[] { _a, _b, _c }.Where(p => ids.Contains(p.Id)).ToList());
        _invitations.Setup(r => r.ListPendingForEmployeeAsync(TenantId, Me, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _invites.Where(i => i.InvitedEmployeeId == Me && i.Status == ProjectInvitationStatuses.Pending).ToList());
        _identity.Setup(i => i.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<Guid> ids, CancellationToken _) => ids.Distinct().ToDictionary(id => id, _ => "Someone"));
        _tasks.Setup(t => t.GetObjectiveIdsByTaskIdsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid>());
    }

    private readonly HashSet<Guid> _achieved = new();

    private MonitorAlertAudience Audience() => new(_snapshots.Object, _hierarchy.Object, _projects.Object);

    private WorkInboxSourceProvider Provider() => new(
        _projects.Object, _invitations.Object, _requests.Object,
        new ApprovalFeedBuilder(_identity.Object, _requests.Object, _hierarchy.Object, _invitations.Object, _tasks.Object, _comments),
        _alerts.Object, Audience(), _hierarchy.Object, _tasks.Object, _identity.Object, _sender.Object, _clock.Object);

    private WorkConversationSubjectResolver Resolver() => new(_requests.Object, _hierarchy.Object, _invitations.Object, _alerts.Object, Audience());

    private WorkApprovalRequest Request(Project p, Guid requester, string status = WorkApprovalRequestStatuses.Pending)
    {
        var module = _trees[p.Id].Module;
        var r = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = p.Id, PositionObjectiveId = module,
            ApproverSource = WorkApprovalSources.Hierarchy, ApproverEmployeeId = Approver, RequestedByEmployeeId = requester,
            Status = status, ActionType = WorkActionTypes.ModuleEdit, TargetType = WorkTargetTypes.Module, TargetId = module,
            TargetTitle = $"{p.Name} change", PayloadJson = "{}", CreatedAt = Now.AddHours(-1)
        };
        _rows.Add(r);
        _requests.Setup(x => x.GetByIdForTenantAsync(TenantId, r.Id, It.IsAny<CancellationToken>())).ReturnsAsync(r);
        return r;
    }

    private MonitorAlert Alert(Project p, Guid? subject = null, DateTimeOffset? resolvedAt = null)
    {
        var a = new MonitorAlert
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = p.Id, TargetType = MonitorTargetTypes.Module,
            TargetId = _trees[p.Id].Module, TargetTitle = "Payments", RuleCode = "module_over_capacity", Message = "Over capacity",
            SubjectEmployeeId = subject, FirstDetectedAt = Now.AddDays(-1), LastSeenAt = Now, ResolvedAt = resolvedAt
        };
        _alertRows.Add(a);
        _alerts.Setup(x => x.GetByIdAsync(TenantId, a.Id, It.IsAny<CancellationToken>())).ReturnsAsync(a);
        return a;
    }

    private Task<IReadOnlyList<InboxItem>> List() => Provider().ListAsync(TenantId, new InboxQuery(), Me, default);

    // ---- list ---------------------------------------------------------------------------------

    [Fact]
    public async Task Lists_requests_from_every_project_the_caller_is_a_member_of()
    {
        _memberOf.AddRange([_a, _b]);
        var inA = Request(_a, Me);
        var inB = Request(_b, Me);

        var items = await List();

        items.Select(i => (i.Id, i.ContextId, i.ContextName)).Should().BeEquivalentTo(new[]
        {
            (inA.Id, (Guid?)_a.Id, (string?)"Apollo"), (inB.Id, (Guid?)_b.Id, (string?)"Borealis")
        });
        items.Should().AllSatisfy(i =>
        {
            i.Source.Should().Be("work");
            i.Kind.Should().Be("approval");
            i.Category.Should().Be(InboxCategories.Request);
            i.Direction.Should().Be("sent");
            i.IsPending.Should().BeTrue();
        });
    }

    [Fact]
    public async Task A_caller_in_project_A_only_sees_nothing_from_project_B()
    {
        _memberOf.Add(_a);
        Request(_a, Me);
        Request(_b, Me);

        var items = await List();

        items.Should().ContainSingle().Which.ContextId.Should().Be(_a.Id);
        _requests.Verify(r => r.ListByProjectAsync(TenantId, _b.Id, null, null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_received_request_offers_approve_and_reject_and_carries_the_module_facet()
    {
        _memberOf.Add(_a);
        // Me owns nothing; make Me the approver by giving Me the module.
        var r = Request(_a, Requester);
        r.ApproverEmployeeId = Me;
        _hierarchy.Setup(h => h.LoadTreeAsync(TenantId, _a.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new ProjectModuleTree(
        [
            new Objective { Id = _trees[_a.Id].Root, OwnerId = Guid.NewGuid(), IsDefault = true },
            new Objective { Id = _trees[_a.Id].Module, ParentObjectiveId = _trees[_a.Id].Root, OwnerId = Me, Title = "Payments" }
        ]));

        var item = (await List()).Single();

        item.Direction.Should().Be("received");
        item.AvailableActions.Select(a => a.Key).Should().Equal("approve", "reject");
        item.AvailableActions.Single(a => a.Key == "reject").NeedsComment.Should().BeTrue();
        item.Facets["module"].Should().Equal(_trees[_a.Id].Module.ToString());
    }

    [Fact]
    public async Task A_pending_invitation_in_a_project_the_caller_is_not_yet_a_member_of_is_listed()
    {
        var invite = new ProjectMemberInvitation
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = _c.Id, ObjectiveId = _trees[_c.Id].Module,
            InvitedEmployeeId = Me, InvitedById = Approver, Status = ProjectInvitationStatuses.Pending, InviteType = "member", CreatedAt = Now
        };
        _invites.Add(invite);

        var item = (await List()).Single();

        item.Kind.Should().Be("invitation");
        item.ContextId.Should().Be(_c.Id);
        item.AvailableActions.Select(a => a.Key).Should().Equal("accept_invitation", "decline_invitation");
    }

    [Fact]
    public async Task Alerts_are_listed_only_for_their_recipients()
    {
        _memberOf.Add(_a);
        var forRootOwner = Alert(_a);
        var aboutMe = Alert(_a, subject: Me);

        (await List()).Select(i => i.Id).Should().Equal(aboutMe.Id);

        var rootOwner = _trees[_a.Id].RootOwner;
        _projects.Setup(r => r.ListForMemberAsync(TenantId, rootOwner, 0, int.MaxValue, null, "asc", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Project> { _a }, 1));
        _invitations.Setup(r => r.ListForProjectAndEmployeeAsync(TenantId, _a.Id, rootOwner, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _invitations.Setup(r => r.ListPendingForEmployeeAsync(TenantId, rootOwner, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var ownerItems = await Provider().ListAsync(TenantId, new InboxQuery(), rootOwner, default);
        ownerItems.Select(i => i.Id).Should().BeEquivalentTo(new[] { forRootOwner.Id, aboutMe.Id });
        ownerItems.Should().AllSatisfy(i =>
        {
            i.Category.Should().Be(InboxCategories.Alert);
            i.Kind.Should().Be("monitor_alert");
            i.Status.Should().Be("open");
            i.IsPending.Should().BeTrue();
            i.AvailableActions.Select(a => a.Key).Should().Equal("open_target");
        });
    }

    [Fact]
    public async Task Resolved_alerts_and_alerts_on_achieved_modules_are_shown_but_not_pending()
    {
        _memberOf.Add(_a);
        var resolved = Alert(_a, subject: Me, resolvedAt: Now.AddDays(-2));
        _achieved.Add(_trees[_a.Id].Module);
        var achieved = Alert(_a, subject: Me);

        var items = await List();

        items.Single(i => i.Id == resolved.Id).Should().Match<InboxItem>(i => i.Status == "resolved" && !i.IsPending);
        items.Single(i => i.Id == achieved.Id).Should().Match<InboxItem>(i => i.Status == "open" && !i.IsPending);
    }

    [Fact]
    public async Task Filter_fields_offer_the_callers_projects_types_and_modules()
    {
        _memberOf.AddRange([_a, _b]);
        Request(_a, Me);
        Alert(_b, subject: Me);

        var fields = await Provider().GetFilterFieldsAsync(TenantId, Me, default);

        fields.Single(f => f.Key == "project").Options.Select(o => o.Label).Should().Equal("Apollo", "Borealis");
        fields.Single(f => f.Key == "type").Options.Should().Contain(o => o.Value == "module.edit" && o.Label == "Module edit")
            .And.Contain(o => o.Value == "monitor.module_over_capacity" && o.Label == "Module over capacity");
        fields.Single(f => f.Key == "module").Options.Should().HaveCount(2);
    }

    // ---- actions ------------------------------------------------------------------------------

    private static JsonElement Body(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Approve_maps_to_the_existing_decide_command_with_the_edited_payload_and_comment()
    {
        var r = Request(_a, Requester);
        _sender.Setup(s => s.Send(It.IsAny<DecideWorkApprovalRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkApprovalRequestResponse>.Success(null!));

        var result = await Provider().ExecuteActionAsync(TenantId, "approval", r.Id, "approve",
            Body("{\"comment\":\"ok\",\"editedPayloadJson\":\"{\\\"title\\\":\\\"x\\\"}\"}"), Me, default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AffectedEmployeeIds.Should().BeEquivalentTo(new[] { Requester, Approver, Me });
        _sender.Verify(s => s.Send(new DecideWorkApprovalRequestCommand(r.Id, WorkApprovalDecision.Approve, "{\"title\":\"x\"}", "ok"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("cancel")]
    public async Task Reject_and_cancel_map_to_the_decide_command(string action)
    {
        var r = Request(_a, Requester);
        _sender.Setup(s => s.Send(It.IsAny<DecideWorkApprovalRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkApprovalRequestResponse>.Success(null!));

        await Provider().ExecuteActionAsync(TenantId, "approval", r.Id, action, Body("{\"comment\":\"no\"}"), Me, default);

        var decision = action == "reject" ? WorkApprovalDecision.Reject : WorkApprovalDecision.Cancel;
        _sender.Verify(s => s.Send(It.Is<DecideWorkApprovalRequestCommand>(c => c.RequestId == r.Id && c.Decision == decision),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Revert_maps_to_the_revert_command()
    {
        var r = Request(_a, Requester);
        _sender.Setup(s => s.Send(It.IsAny<RevertWorkApprovalRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkApprovalRequestResponse>.Success(null!));

        (await Provider().ExecuteActionAsync(TenantId, "approval", r.Id, "revert", null, Me, default)).IsSuccess.Should().BeTrue();

        _sender.Verify(s => s.Send(new RevertWorkApprovalRequestCommand(r.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Invitation_actions_map_to_accept_reject_and_cancel()
    {
        var id = Guid.NewGuid();
        _sender.Setup(s => s.Send(It.IsAny<IRequest<Result>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        await Provider().ExecuteActionAsync(TenantId, "invitation", id, "accept_invitation", null, Me, default);
        await Provider().ExecuteActionAsync(TenantId, "invitation", id, "decline_invitation", null, Me, default);
        await Provider().ExecuteActionAsync(TenantId, "invitation", id, "cancel", null, Me, default);

        _sender.Verify(s => s.Send(new AcceptObjectiveInvitationCommand(id), It.IsAny<CancellationToken>()), Times.Once);
        _sender.Verify(s => s.Send(new RejectObjectiveInvitationCommand(id), It.IsAny<CancellationToken>()), Times.Once);
        _sender.Verify(s => s.Send(new CancelObjectiveInvitationCommand(id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_member_added_to_the_chat_cannot_approve_because_the_WM_command_refuses()
    {
        var r = Request(_a, Requester);
        _sender.Setup(s => s.Send(It.IsAny<DecideWorkApprovalRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkApprovalRequestResponse>.Forbidden("You cannot decide this request."));

        var result = await Provider().ExecuteActionAsync(TenantId, "approval", r.Id, "approve", null, Stranger, default);

        result.StatusCode.Should().Be(403);
        result.Error.Should().Be("You cannot decide this request.");
    }

    [Theory]
    [InlineData("approval", "accept_invitation")]
    [InlineData("monitor_alert", "open_target")]
    [InlineData("invitation", "approve")]
    public async Task Actions_that_do_not_apply_to_the_kind_are_rejected(string kind, string action)
    {
        (await Provider().ExecuteActionAsync(TenantId, kind, Guid.NewGuid(), action, null, Me, default)).StatusCode.Should().Be(422);
        _sender.VerifyNoOtherCalls();
    }

    // ---- detail -------------------------------------------------------------------------------

    [Fact]
    public async Task An_alert_detail_is_hidden_from_non_recipients()
    {
        var a = Alert(_a);

        (await Provider().GetDetailAsync(TenantId, "monitor_alert", a.Id, Stranger, default)).Should().BeNull();
        var detail = await Provider().GetDetailAsync(TenantId, "monitor_alert", a.Id, _trees[_a.Id].RootOwner, default);
        detail!.Steps.Select(s => s.Key).Should().Equal("detected", "notified", "resolved");
        detail.Explanation.Should().Be("Over capacity");
        detail.Target!.Route.Should().Equal("/work", _a.Id.ToString(), "milestones", _trees[_a.Id].Module.ToString(), "tree");
    }

    [Fact]
    public async Task An_approval_detail_the_caller_cannot_see_is_not_found()
    {
        _sender.Setup(s => s.Send(It.IsAny<ONEVO.Application.Features.WorkManagement.Approvals.Queries.GetApprovalDetail.GetApprovalDetailQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ApprovalDetailResponse>.NotFound("Approval request not found."));

        (await Provider().GetDetailAsync(TenantId, "approval", Guid.NewGuid(), Stranger, default)).Should().BeNull();
        (await Provider().GetDetailAsync(TenantId, "unknown", Guid.NewGuid(), Me, default)).Should().BeNull();
    }

    // ---- conversation base participants ---------------------------------------------------------

    [Fact]
    public async Task Approval_chat_base_is_the_requester_and_todays_position_holder_so_a_transfer_moves_it()
    {
        var r = Request(_a, Requester);

        (await Resolver().ResolveAsync(TenantId, new ItemRef("work", "approval", r.Id), default))!
            .BaseParticipantEmployeeIds.Should().BeEquivalentTo(new[] { Requester, Approver });

        _hierarchy.Setup(h => h.LoadTreeAsync(TenantId, _a.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new ProjectModuleTree(
        [
            new Objective { Id = _trees[_a.Id].Root, OwnerId = Guid.NewGuid(), IsDefault = true },
            new Objective { Id = _trees[_a.Id].Module, ParentObjectiveId = _trees[_a.Id].Root, OwnerId = NewHolder, Title = "Payments" }
        ]));
        (await Resolver().ResolveAsync(TenantId, new ItemRef("work", "approval", r.Id), default))!
            .BaseParticipantEmployeeIds.Should().BeEquivalentTo(new[] { Requester, NewHolder });
    }

    [Fact]
    public async Task HR_routed_approvals_use_the_stored_approver_and_decided_ones_include_the_decider()
    {
        var r = Request(_a, Requester, WorkApprovalRequestStatuses.Approved);
        r.ApproverSource = WorkApprovalSources.Hr;
        r.DecidedByEmployeeId = Stranger;

        var subject = await Resolver().ResolveAsync(TenantId, new ItemRef("work", "approval", r.Id), default);

        subject!.BaseParticipantEmployeeIds.Should().BeEquivalentTo(new[] { Requester, Approver, Stranger });
        subject.Title.Should().Be("Module edit: Apollo change");
        subject.ProjectId.Should().Be(_a.Id);
    }

    [Fact]
    public async Task Invitation_and_alert_chats_have_their_own_base_people()
    {
        var invite = new ProjectMemberInvitation
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = _a.Id, ObjectiveId = _trees[_a.Id].Module,
            InvitedEmployeeId = Me, InvitedById = Approver, Status = ProjectInvitationStatuses.Pending
        };
        _invitations.Setup(x => x.GetByIdForTenantAsync(TenantId, invite.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invite);
        var alert = Alert(_a, subject: Me);

        (await Resolver().ResolveAsync(TenantId, new ItemRef("work", "invitation", invite.Id), default))!
            .BaseParticipantEmployeeIds.Should().BeEquivalentTo(new[] { Approver, Me });
        (await Resolver().ResolveAsync(TenantId, new ItemRef("work", "monitor_alert", alert.Id), default))!
            .BaseParticipantEmployeeIds.Should().BeEquivalentTo(new[] { _trees[_a.Id].RootOwner, Me });
        (await Resolver().ResolveAsync(TenantId, new ItemRef("work", "approval", Guid.NewGuid()), default)).Should().BeNull();
        (await Resolver().ResolveAsync(TenantId, new ItemRef("work", "sprint", Guid.NewGuid()), default)).Should().BeNull();
    }
}
