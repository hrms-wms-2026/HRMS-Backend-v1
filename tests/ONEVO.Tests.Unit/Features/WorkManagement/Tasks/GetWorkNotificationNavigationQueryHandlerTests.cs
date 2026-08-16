using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetWorkNotificationNavigation;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetWorkNotificationNavigationQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private static Mock<ICurrentUser> AuthUser()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        return currentUser;
    }

    [Fact]
    public async Task Handle_TaskCreationRequest_ReturnsBoardTab()
    {
        var requestId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var requests = new Mock<ITaskCreationRequestRepository>();
        requests.Setup(x => x.GetByIdForTenantAsync(TenantId, requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskCreationRequest
            {
                Id = requestId, TenantId = TenantId, ObjectiveId = ObjectiveId, CreatedTaskId = taskId
            });

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Objective { Id = ObjectiveId, ProjectId = ProjectId, Title = "M1" });

        var handler = new GetWorkNotificationNavigationQueryHandler(
            AuthUser().Object, new Mock<IWorkTaskRepository>().Object, requests.Object,
            new Mock<IObjectiveChangeRequestRepository>().Object, objectives.Object);

        var result = await handler.Handle(
            new GetWorkNotificationNavigationQuery("task_creation_request", requestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProjectId, result.Value!.ProjectId);
        Assert.Equal(ObjectiveId, result.Value.ObjectiveId);
        Assert.Equal(taskId, result.Value.TaskId);
        Assert.Equal("board", result.Value.TargetTab);
    }

    [Fact]
    public async Task Handle_ObjectiveChangeRequest_ReturnsApprovalsTab()
    {
        var changeId = Guid.NewGuid();
        var changes = new Mock<IObjectiveChangeRequestRepository>();
        changes.Setup(x => x.GetByIdForTenantAsync(TenantId, changeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ObjectiveChangeRequest
            {
                Id = changeId, TenantId = TenantId, ObjectiveId = ObjectiveId, RequestType = "extend_allocation"
            });

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Objective { Id = ObjectiveId, ProjectId = ProjectId, Title = "M1" });

        var handler = new GetWorkNotificationNavigationQueryHandler(
            AuthUser().Object, new Mock<IWorkTaskRepository>().Object, new Mock<ITaskCreationRequestRepository>().Object,
            changes.Object, objectives.Object);

        var result = await handler.Handle(
            new GetWorkNotificationNavigationQuery("allocation_extend", changeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("approvals", result.Value!.TargetTab);
        Assert.Null(result.Value.TaskId);
    }
}
