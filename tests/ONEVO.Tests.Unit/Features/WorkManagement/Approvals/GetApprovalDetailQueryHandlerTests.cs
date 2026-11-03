using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Queries.GetApprovalDetail;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public class GetApprovalDetailQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();
    private readonly Guid _root = Guid.NewGuid();
    private readonly Guid _p = Guid.NewGuid();
    private readonly Guid _taskId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IWorkApprovalRequestRepository> _requests = new();
    private readonly Mock<IWorkHierarchyService> _hierarchy = new();
    private readonly Mock<IProjectMemberInvitationRepository> _invitations = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<ISprintRepository> _sprints = new();
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly WorkApprovalRequest _request;

    public GetApprovalDetailQueryHandlerTests()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [Requester] = "Bala", [Owner] = "Anu", [Stranger] = "Chen" });
        _hierarchy.Setup(x => x.LoadTreeAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectModuleTree(new[]
            {
                new Objective { Id = _root, OwnerId = Guid.NewGuid(), IsDefault = true, Title = "Root" },
                new Objective { Id = _p, ParentObjectiveId = _root, OwnerId = Owner, Title = "Payments", AllocatedHours = 120m },
            }));
        _projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = ProjectId, Name = "Apollo" });
        _tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, _taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkTask { Id = _taskId, ObjectiveId = _p, Title = "Old", Priority = "high", DueDate = new DateOnly(2026, 10, 1) });
        _request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, PositionObjectiveId = _p,
            ApproverSource = WorkApprovalSources.Hierarchy, ApproverEmployeeId = Owner, RequestedByEmployeeId = Requester,
            Status = WorkApprovalRequestStatuses.Pending, ActionType = WorkActionTypes.TaskEdit, TargetType = WorkTargetTypes.Task,
            TargetId = _taskId, TargetTitle = "Old",
            PayloadJson = """{"Title":"New","Priority":"high","DueDate":"2026-10-05","Reason":"client asked"}"""
        };
        _requests.Setup(x => x.GetByIdForTenantAsync(TenantId, _request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_request);
    }

    private void Caller(Guid employeeId)
        => _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(employeeId);

    private GetApprovalDetailQueryHandler Build() => new(
        _currentUser.Object, _identity.Object, _requests.Object, _hierarchy.Object, _invitations.Object,
        _tasks.Object, _sprints.Object, _projects.Object);

    [Fact]
    public async Task Stranger_gets_404()
    {
        Caller(Stranger);
        (await Build().Handle(new GetApprovalDetailQuery(_request.Id, "engine"), default)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Approver_can_edit_a_pending_task_edit_and_sees_the_diff()
    {
        Caller(Owner);

        var result = await Build().Handle(new GetApprovalDetailQuery(_request.Id, "engine"), default);

        result.IsSuccess.Should().BeTrue();
        var detail = result.Value!;
        detail.CanEditPayload.Should().BeTrue();
        detail.Item.CanDecide.Should().BeTrue();
        detail.Item.ModuleTitle.Should().Be("Payments");
        detail.Note.Should().Be("client asked");
        detail.RequestedPayloadJson.Should().Be(_request.PayloadJson);
        var title = detail.Fields.Single(f => f.Key == "Title");
        title.Current.Should().Be("Old");
        title.Requested.Should().Be("New");
        title.Changed.Should().BeTrue();
        title.Editable.Should().BeTrue();
        detail.Fields.Single(f => f.Key == "Priority").Changed.Should().BeFalse();
        detail.Fields.Single(f => f.Key == "DueDate").Current.Should().Be("2026-10-01");
    }

    [Fact]
    public async Task Requester_cannot_edit_payload()
    {
        Caller(Requester);

        var detail = (await Build().Handle(new GetApprovalDetailQuery(_request.Id, "engine"), default)).Value!;

        detail.CanEditPayload.Should().BeFalse();
        detail.Item.CanCancel.Should().BeTrue();
        detail.Fields.Should().OnlyContain(f => !f.Editable);
    }

    [Fact]
    public async Task Allocation_detail_carries_current_allocated_hours()
    {
        Caller(Requester);
        _request.ActionType = WorkActionTypes.ModuleAllocationExtend;
        _request.TargetType = WorkTargetTypes.Module;
        _request.TargetId = _p;
        _request.PayloadJson = """{"requestedAdditionalHours":35,"reason":"scope"}""";

        var detail = (await Build().Handle(new GetApprovalDetailQuery(_request.Id, "engine"), default)).Value!;

        detail.CurrentAllocatedHours.Should().Be(120m);
        detail.Fields.Should().ContainSingle(f => f.Key == "requestedAdditionalHours");
    }

    [Fact]
    public async Task Invitation_returns_invitation_block_and_no_fields()
    {
        var invitation = new ProjectMemberInvitation
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = _p, InvitedEmployeeId = Stranger,
            InvitedById = Owner, InviteType = ProjectInvitationTypes.Member, Status = ProjectInvitationStatuses.Pending,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };
        _invitations.Setup(x => x.GetByIdForTenantAsync(TenantId, invitation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);
        Caller(Stranger);

        var detail = (await Build().Handle(new GetApprovalDetailQuery(invitation.Id, "invitation"), default)).Value!;

        detail.Fields.Should().BeEmpty();
        detail.Item.CanDecide.Should().BeTrue();
        detail.Invitation!.InviteeName.Should().Be("Chen");
        detail.Invitation.InvitedByName.Should().Be("Anu");
        detail.Invitation.ModuleTitle.Should().Be("Payments");
        detail.Invitation.InviteType.Should().Be("member");

        Caller(Requester);
        (await Build().Handle(new GetApprovalDetailQuery(invitation.Id, "invitation"), default)).StatusCode.Should().Be(404);
    }

    // ---- meeting.create / meeting.move: the D5 snapshot, to receivers only (meetings-as-tasks D5, spec 5 access tiers) ----

    private static readonly string[] SnapshotKeys =
    [
        "title", "description", "start", "durationMinutes", "timezone", "recurrenceSummary", "organizerName", "modulePath",
        "participantNames", "guestEmails", "platform",
    ];

    private void UseMeetingCreate()
    {
        var start = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
        var form = new CreateMeetingRequest(ProjectId, _p, "Design review", "<p>Agenda</p>", null, false, start, 45, false, "UTC", null,
            [Owner], ["g@x.com"], "zoom");
        var snapshot = new MeetingApprovalSnapshot("Design review", "<p>Agenda</p>", start, 45, "UTC", "Does not repeat", "Bala",
            "Root / Payments", ["Anu"], ["g@x.com"], "zoom");
        _request.ActionType = WorkActionTypes.MeetingCreate;
        _request.TargetType = WorkTargetTypes.Meeting;
        _request.TargetId = null;
        _request.TargetTitle = "Design review";
        _request.PayloadJson = JsonSerializer.Serialize(new MeetingCreatePayload(form, snapshot), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    [Fact]
    public async Task Meeting_create_approver_sees_only_the_snapshot()
    {
        UseMeetingCreate();
        Caller(Owner);

        var detail = (await Build().Handle(new GetApprovalDetailQuery(_request.Id, "engine"), default)).Value!;

        detail.RequestedPayloadJson.Should().BeNull();
        detail.AppliedPayloadJson.Should().BeNull();
        detail.Fields.Should().BeEmpty();
        detail.CanEditPayload.Should().BeFalse();
        detail.ExtraJson.Should().NotBeNull();
        using var extra = JsonDocument.Parse(detail.ExtraJson!);
        extra.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("snapshot");
        var snapshot = extra.RootElement.GetProperty("snapshot");
        snapshot.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(SnapshotKeys);
        snapshot.GetProperty("title").GetString().Should().Be("Design review");
        snapshot.GetProperty("guestEmails")[0].GetString().Should().Be("g@x.com");
        detail.ExtraJson.Should().NotContain("participantEmployeeIds").And.NotContain("\"form\"");
    }

    [Fact]
    public async Task Meeting_create_requester_gets_no_snapshot_and_no_payload()
    {
        UseMeetingCreate();
        Caller(Requester);

        var result = await Build().Handle(new GetApprovalDetailQuery(_request.Id, "engine"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ExtraJson.Should().BeNull();
        result.Value.RequestedPayloadJson.Should().BeNull();
        result.Value.AppliedPayloadJson.Should().BeNull();
    }

    [Fact]
    public async Task Meeting_requester_who_can_decide_still_gets_no_snapshot()
    {
        UseMeetingCreate();
        // The requester owns the root module, so the decision rules would let them decide.
        _hierarchy.Setup(x => x.LoadTreeAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectModuleTree(new[]
            {
                new Objective { Id = _root, OwnerId = Requester, IsDefault = true, Title = "Root" },
                new Objective { Id = _p, ParentObjectiveId = _root, OwnerId = Owner, Title = "Payments" },
            }));
        Caller(Requester);

        var detail = (await Build().Handle(new GetApprovalDetailQuery(_request.Id, "engine"), default)).Value!;

        detail.ExtraJson.Should().BeNull();
        detail.RequestedPayloadJson.Should().BeNull();
    }

    [Fact]
    public async Task Meeting_request_stranger_gets_404()
    {
        UseMeetingCreate();
        Caller(Stranger);

        var result = await Build().Handle(new GetApprovalDetailQuery(_request.Id, "engine"), default);

        result.StatusCode.Should().Be(404);
        result.Error.Should().Be("Approval request not found.");
    }

    [Fact]
    public async Task Meeting_create_decided_approver_keeps_the_snapshot()
    {
        UseMeetingCreate();
        _request.Status = WorkApprovalRequestStatuses.Approved;
        _request.DecidedByEmployeeId = Owner;
        _request.DecidedAt = DateTimeOffset.UtcNow;
        Caller(Owner);

        var detail = (await Build().Handle(new GetApprovalDetailQuery(_request.Id, "engine"), default)).Value!;

        detail.ExtraJson.Should().Contain("\"title\":\"Design review\"");
    }

    [Fact]
    public async Task Meeting_move_without_a_stored_snapshot_shows_none()
    {
        _request.ActionType = WorkActionTypes.MeetingMove;
        _request.TargetType = WorkTargetTypes.Meeting;
        _request.TargetId = Guid.NewGuid();
        _request.PayloadJson = $$"""{"seriesId":"{{_request.TargetId}}","originalStart":"2026-11-02T03:30:00Z","scope":"this","projectId":"{{ProjectId}}","objectiveId":"{{_p}}"}""";
        Caller(Owner);

        var result = await Build().Handle(new GetApprovalDetailQuery(_request.Id, "engine"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ExtraJson.Should().BeNull();
        result.Value.RequestedPayloadJson.Should().BeNull();
    }
}
