using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetWorkNotificationNavigation;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetWorkNotificationNavigationQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<IWorkApprovalRequestRepository> _workApprovals = new();
    private readonly Mock<IObjectiveChangeRequestRepository> _changes = new();
    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<IProjectMemberInvitationRepository> _invitations = new();
    private readonly Mock<ITaskStatusChangeRequestRepository> _statusChanges = new();

    public GetWorkNotificationNavigationQueryHandlerTests()
    {
        _objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Objective { Id = ObjectiveId, ProjectId = ProjectId, Title = "M1" });
    }

    private GetWorkNotificationNavigationQueryHandler Build()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        return new GetWorkNotificationNavigationQueryHandler(
            currentUser.Object, _tasks.Object, _workApprovals.Object, _changes.Object, _objectives.Object,
            _invitations.Object, _statusChanges.Object);
    }

    [Theory]
    [InlineData("work_approval_request")]
    [InlineData("task_creation_request")] // old bell notifications: ids survive the data migration
    [InlineData("task_edit_request")]
    public async Task Navigation_WorkApprovalRequest_OpensProjectApprovalsTab(string relatedEntityType)
    {
        var requestId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        _workApprovals.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkApprovalRequest { Id = requestId, TenantId = TenantId, ProjectId = ProjectId, ActionType = WorkActionTypes.TaskCreate });
        _objectives.Setup(x => x.GetDefaultByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Objective { Id = rootId, ProjectId = ProjectId, IsDefault = true });

        var result = await Build().Handle(new GetWorkNotificationNavigationQuery(relatedEntityType, requestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProjectId, result.Value!.ProjectId);
        Assert.Equal(rootId, result.Value.ObjectiveId);
        Assert.Null(result.Value.TaskId);
        Assert.Equal("approvals", result.Value.TargetTab);
    }

    [Fact]
    public async Task Navigation_WorkApprovalRequest_Missing_NotFound()
    {
        var result = await Build().Handle(new GetWorkNotificationNavigationQuery("work_approval_request", Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveChangeRequest_ReturnsApprovalsTab()
    {
        var changeId = Guid.NewGuid();
        _changes.Setup(x => x.GetByIdForTenantAsync(TenantId, changeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ObjectiveChangeRequest
            {
                Id = changeId, TenantId = TenantId, ObjectiveId = ObjectiveId, RequestType = "extend_allocation"
            });

        var result = await Build().Handle(
            new GetWorkNotificationNavigationQuery("allocation_extend", changeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("approvals", result.Value!.TargetTab);
        Assert.Null(result.Value.TaskId);
    }

    [Fact]
    public async Task Handle_ProjectMemberInvitation_ReturnsTreeTab()
    {
        var invitationId = Guid.NewGuid();
        _invitations.Setup(x => x.GetByIdForTenantAsync(TenantId, invitationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectMemberInvitation
            {
                Id = invitationId, TenantId = TenantId, ObjectiveId = ObjectiveId, ProjectId = ProjectId
            });

        var result = await Build().Handle(
            new GetWorkNotificationNavigationQuery("project_member_invitation", invitationId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProjectId, result.Value!.ProjectId);
        Assert.Equal(ObjectiveId, result.Value.ObjectiveId);
        Assert.Null(result.Value.TaskId);
        Assert.Equal("tree", result.Value.TargetTab);
    }
}
