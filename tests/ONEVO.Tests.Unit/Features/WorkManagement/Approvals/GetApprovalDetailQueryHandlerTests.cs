using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Queries.GetApprovalDetail;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
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
}
