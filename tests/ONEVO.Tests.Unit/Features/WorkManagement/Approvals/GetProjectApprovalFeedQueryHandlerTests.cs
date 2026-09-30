using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.Queries.GetProjectApprovalFeed;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public class GetProjectApprovalFeedQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();     // owns module P
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private readonly Guid _root = Guid.NewGuid();
    private readonly Guid _p = Guid.NewGuid();
    private readonly Guid _q = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IWorkApprovalRequestRepository> _requests = new();
    private readonly Mock<IWorkHierarchyService> _hierarchy = new();
    private readonly Mock<IProjectMemberInvitationRepository> _invitations = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly List<WorkApprovalRequest> _rows = new();
    private readonly List<ProjectMemberInvitation> _invites = new();
    private readonly ApprovalCommentHandlersTests.InMemoryApprovalCommentRepository _comments = new();

    public GetProjectApprovalFeedQueryHandlerTests()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [Requester] = "Bala", [Owner] = "Anu" });
        _hierarchy.Setup(x => x.LoadTreeAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectModuleTree(new[]
            {
                new Objective { Id = _root, OwnerId = Guid.NewGuid(), IsDefault = true, Title = "Root" },
                new Objective { Id = _p, ParentObjectiveId = _root, OwnerId = Owner, Title = "Payments" },
                new Objective { Id = _q, ParentObjectiveId = _root, OwnerId = Guid.NewGuid(), Title = "Reports" },
            }));
        _projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = ProjectId, Name = "Apollo" });
        _requests.Setup(x => x.ListByProjectAsync(TenantId, ProjectId, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(_rows);
        _invitations.Setup(x => x.ListForProjectAndEmployeeAsync(TenantId, ProjectId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(_invites);
        _tasks.Setup(x => x.GetObjectiveIdsByTaskIdsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid>());
    }

    private void Caller(Guid employeeId)
        => _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(employeeId);

    private WorkApprovalRequest Add(Guid position, string status = WorkApprovalRequestStatuses.Pending, DateTimeOffset? createdAt = null)
    {
        var r = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, PositionObjectiveId = position,
            ApproverSource = WorkApprovalSources.Hierarchy, ApproverEmployeeId = Owner, RequestedByEmployeeId = Requester,
            Status = status, ActionType = WorkActionTypes.ModuleEdit, TargetType = WorkTargetTypes.Module,
            TargetId = position, TargetTitle = "t", PayloadJson = "{}", CreatedAt = createdAt ?? DateTimeOffset.UtcNow
        };
        _rows.Add(r);
        return r;
    }

    private GetProjectApprovalFeedQueryHandler Build() => new(
        _currentUser.Object, _identity.Object, _requests.Object, _hierarchy.Object,
        _invitations.Object, _tasks.Object, _projects.Object, _comments);

    private async Task<IReadOnlyList<ApprovalFeedItemResponse>> Run()
    {
        var result = await Build().Handle(new GetProjectApprovalFeedQuery(ProjectId), default);
        result.IsSuccess.Should().BeTrue();
        return result.Value!;
    }

    [Fact]
    public async Task Sent_request_is_cancellable_not_decidable()
    {
        Caller(Requester);
        var r = Add(_p);

        var item = (await Run()).Single();

        item.Id.Should().Be(r.Id);
        item.Direction.Should().Be("sent");
        item.CanCancel.Should().BeTrue();
        item.CanDecide.Should().BeFalse();
        item.ModuleTitle.Should().Be("Payments");
        item.ProjectName.Should().Be("Apollo");
        item.RequestedByName.Should().Be("Bala");
        item.Source.Should().Be(ApprovalFeedSources.Engine);
    }

    [Fact]
    public async Task Request_the_caller_can_decide_is_received_and_decidable()
    {
        Caller(Owner);
        Add(_p);

        var item = (await Run()).Single();

        item.Direction.Should().Be("received");
        item.CanDecide.Should().BeTrue();
        item.CanCancel.Should().BeFalse();
    }

    [Fact]
    public async Task Request_between_two_other_people_is_hidden()
    {
        Caller(Other);
        var r = Add(_q);
        r.ApproverEmployeeId = Guid.NewGuid();

        (await Run()).Should().BeEmpty();
    }

    [Fact]
    public async Task Decided_request_the_caller_decided_stays_as_received_history()
    {
        var decider = Guid.NewGuid();
        Caller(decider);
        var r = Add(_q, WorkApprovalRequestStatuses.Rejected);
        r.ApproverEmployeeId = Guid.NewGuid();
        r.DecidedByEmployeeId = decider;
        r.DecisionComment = "too big";

        var item = (await Run()).Single();

        item.Direction.Should().Be("received");
        item.CanDecide.Should().BeFalse();
        item.Status.Should().Be(WorkApprovalRequestStatuses.Rejected);
        item.DecisionComment.Should().Be("too big");
    }

    [Fact]
    public async Task Accepted_invitation_maps_to_approved_for_the_invitee()
    {
        Caller(Other);
        _invites.Add(new ProjectMemberInvitation
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = _p, InvitedEmployeeId = Other,
            InvitedById = Owner, InviteType = ProjectInvitationTypes.Leader, Status = ProjectInvitationStatuses.Accepted,
            CreatedAt = DateTimeOffset.UtcNow, DecidedAt = DateTimeOffset.UtcNow
        });

        var item = (await Run()).Single();

        item.Source.Should().Be(ApprovalFeedSources.Invitation);
        item.ActionType.Should().Be("module.invitation");
        item.Status.Should().Be(WorkApprovalRequestStatuses.Approved);
        item.Direction.Should().Be("received");
        item.TargetTitle.Should().Be("Payments");
        item.DecidedById.Should().Be(Other);
        item.Summary.Should().Be("Invited as leader");
        item.CanDecide.Should().BeFalse();
    }

    [Fact]
    public async Task Pending_invitation_is_decidable_by_the_invitee_only()
    {
        Caller(Owner);
        _invites.Add(new ProjectMemberInvitation
        {
            Id = Guid.NewGuid(), ProjectId = ProjectId, ObjectiveId = _p, InvitedEmployeeId = Other, InvitedById = Owner,
            Status = ProjectInvitationStatuses.Pending, CreatedAt = DateTimeOffset.UtcNow
        });

        var item = (await Run()).Single();

        item.Direction.Should().Be("sent");
        item.CanDecide.Should().BeFalse();
        item.CanCancel.Should().BeFalse();
    }

    [Fact]
    public async Task Pending_rows_come_first_then_newest()
    {
        Caller(Requester);
        var decidedNewest = Add(_p, WorkApprovalRequestStatuses.Approved, DateTimeOffset.UtcNow);
        var pendingOld = Add(_p, createdAt: DateTimeOffset.UtcNow.AddDays(-5));
        var pendingNew = Add(_p, createdAt: DateTimeOffset.UtcNow.AddDays(-1));

        (await Run()).Select(i => i.Id).Should().Equal(pendingNew.Id, pendingOld.Id, decidedNewest.Id);
    }

    [Fact]
    public async Task Task_edit_row_gets_its_module_from_the_task()
    {
        Caller(Requester);
        var taskId = Guid.NewGuid();
        var r = Add(_root);
        r.ActionType = WorkActionTypes.TaskEdit;
        r.TargetType = WorkTargetTypes.Task;
        r.TargetId = taskId;
        _tasks.Setup(x => x.GetObjectiveIdsByTaskIdsAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(taskId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid> { [taskId] = _q });

        var item = (await Run()).Single();

        item.ModuleId.Should().Be(_q);
        item.ModuleTitle.Should().Be("Reports");
    }

    [Fact]
    public async Task Task_create_row_reads_module_from_payload_and_root_module_uses_project_name()
    {
        Caller(Requester);
        var create = Add(_p);
        create.ActionType = WorkActionTypes.TaskCreate;
        create.TargetType = WorkTargetTypes.Task;
        create.TargetId = null;
        create.PayloadJson = $$"""{"ObjectiveId":"{{_root}}","Title":"New"}""";

        var item = (await Run()).Single();

        item.ModuleId.Should().Be(_root);
        item.ModuleTitle.Should().Be("Apollo");
    }

    [Fact]
    public async Task Allocation_row_summary_shows_hours_and_reason()
    {
        Caller(Requester);
        var r = Add(_p);
        r.ActionType = WorkActionTypes.ModuleAllocationExtend;
        r.PayloadJson = """{"requestedAdditionalHours":35,"reason":"scope grew"}""";

        (await Run()).Single().Summary.Should().Be("+35h - scope grew");
    }

    [Fact]
    public async Task Comment_count_reflects_saved_comments_per_subject()
    {
        Caller(Requester);
        var r = Add(_p);
        var invitation = new ProjectMemberInvitation
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = _p, InvitedEmployeeId = Requester,
            InvitedById = Owner, Status = ProjectInvitationStatuses.Pending, CreatedAt = DateTimeOffset.UtcNow
        };
        _invites.Add(invitation);
        foreach (var (type, id) in new[] { ("approval", r.Id), ("approval", r.Id), ("invitation", invitation.Id) })
            await _comments.AddAsync(new WorkApprovalComment { Id = Guid.NewGuid(), TenantId = TenantId, SubjectType = type, SubjectId = id });

        var items = await Run();

        items.Single(i => i.Id == r.Id).CommentCount.Should().Be(2);
        items.Single(i => i.Id == invitation.Id).CommentCount.Should().Be(1);
    }

    [Fact]
    public async Task Missing_project_returns_404()
    {
        Caller(Requester);
        _projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync((Project?)null);

        (await Build().Handle(new GetProjectApprovalFeedQuery(ProjectId), default)).StatusCode.Should().Be(404);
    }
}
