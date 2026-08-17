using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.ApproveTaskCreationRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CancelTaskCreationRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.RejectTaskCreationRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class ApproveTaskCreationRequestCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid DefaultStatusId = Guid.NewGuid();
    private static readonly Guid RequesterEmployeeId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();

    private static TaskCreationRequest PendingRequest(decimal requestedHours) => new()
    {
        Id = RequestId, TenantId = TenantId, ObjectiveId = ObjectiveId,
        RequestedByEmployeeId = RequesterEmployeeId,
        PayloadJson = System.Text.Json.JsonSerializer.Serialize(
            new ONEVO.Application.Features.WorkManagement.Tasks.DTOs.TaskCreationRequestPayload(
                "Title", null, "task", "medium", null, requestedHours, null, SprintId)),
        Status = TaskCreationRequestStatuses.Pending,
        CreatedById = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
    };

    private (ApproveTaskCreationRequestCommandHandler Handler, Mock<IWorkTaskRepository> Tasks, Mock<ITaskCreationRequestRepository> Requests) BuildApprove(
        decimal allocatedHours, decimal existingTaskSum, decimal requestedHours, Guid? callerEmployeeId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerEmployeeId ?? OwnerEmployeeId);

        var pendingRequest = PendingRequest(requestedHours);
        var requests = new Mock<ITaskCreationRequestRepository>();
        requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, RequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingRequest);

        var objective = new Objective
        {
            Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, OwnerId = OwnerEmployeeId,
            AllocatedHours = allocatedHours, IsActive = true, CreatedAt = DateTimeOffset.UtcNow
        };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective>());

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = ProjectId, TenantId = TenantId, Identifier = "WEB", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        projects.Setup(x => x.IncrementAndGetNextTaskNumberAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7L);

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetActiveAllocationSumByObjectiveIdAsync(TenantId, ObjectiveId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTaskSum);

        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByObjectiveIdAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskStatusEntity>
            {
                new() { Id = DefaultStatusId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, Name = "To Do", DisplayOrder = 0, CreatedAt = DateTimeOffset.UtcNow }
            });

        var slack = new ObjectiveAllocationSlackCalculator(objectives.Object, tasks.Object);
        var membership = new Mock<IMilestoneMembershipCoordinator>();
        var notifications = new Mock<INotificationDispatcher>();
        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sprint
            {
                Id = SprintId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId,
                Name = "Sprint 1", Status = SprintStatuses.Active, CreatedAt = DateTimeOffset.UtcNow
            });

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<WorkTaskResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<WorkTaskResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new ApproveTaskCreationRequestCommandHandler(
            currentUser.Object, identity.Object, requests.Object, objectives.Object, projects.Object,
            tasks.Object, statuses.Object, slack, membership.Object, notifications.Object, unitOfWork.Object, sprints.Object);
        return (handler, tasks, requests);
    }

    [Fact]
    public async Task Handle_OwnerWithinSlack_ApprovesAndCreatesTask()
    {
        var (handler, tasks, requests) = BuildApprove(allocatedHours: 100m, existingTaskSum: 40m, requestedHours: 30m);
        var result = await handler.Handle(new ApproveTaskCreationRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("WEB-7", result.Value!.ShortId);
        tasks.Verify(x => x.AddAsync(It.IsAny<WorkTask>(), It.IsAny<CancellationToken>()), Times.Once);
        requests.Verify(x => x.Update(It.Is<TaskCreationRequest>(r => r.Status == TaskCreationRequestStatuses.Approved && r.CreatedTaskId != null)), Times.Once);
    }

    [Fact]
    public async Task Handle_SlackChangedSinceRequestCreated_ReturnsConflict()
    {
        var (handler, tasks, _) = BuildApprove(allocatedHours: 100m, existingTaskSum: 90m, requestedHours: 30m);
        var result = await handler.Handle(new ApproveTaskCreationRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("\"availableSlackHours\"", result.Error);
        Assert.DoesNotContain("\"AvailableSlackHours\"", result.Error);
        tasks.Verify(x => x.AddAsync(It.IsAny<WorkTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NonOwner_ReturnsForbidden()
    {
        var (handler, tasks, requests) = BuildApprove(allocatedHours: 100m, existingTaskSum: 40m, requestedHours: 30m, callerEmployeeId: OtherEmployeeId);
        var result = await handler.Handle(new ApproveTaskCreationRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        tasks.Verify(x => x.AddAsync(It.IsAny<WorkTask>(), It.IsAny<CancellationToken>()), Times.Never);
        requests.Verify(x => x.Update(It.IsAny<TaskCreationRequest>()), Times.Never);
    }
}

public class RejectTaskCreationRequestCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid RequestId = Guid.NewGuid();

    private (RejectTaskCreationRequestCommandHandler Handler, Mock<ITaskCreationRequestRepository> Requests) Build(Guid callerEmployeeId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerEmployeeId);

        var pending = new TaskCreationRequest
        {
            Id = RequestId, TenantId = TenantId, ObjectiveId = ObjectiveId, RequestedByEmployeeId = Guid.NewGuid(),
            PayloadJson = "{}", Status = TaskCreationRequestStatuses.Pending, CreatedById = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
        };
        var requests = new Mock<ITaskCreationRequestRepository>();
        requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, RequestId, It.IsAny<CancellationToken>())).ReturnsAsync(pending);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true, CreatedAt = DateTimeOffset.UtcNow });

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> op, CancellationToken ct) => op(ct));

        var handler = new RejectTaskCreationRequestCommandHandler(
            currentUser.Object, identity.Object, requests.Object, objectives.Object,
            new Mock<IMilestoneMembershipCoordinator>().Object, new Mock<INotificationDispatcher>().Object, unitOfWork.Object);
        return (handler, requests);
    }

    [Fact]
    public async Task Handle_Owner_RejectsWithComment()
    {
        var (handler, requests) = Build(OwnerEmployeeId);
        var result = await handler.Handle(new RejectTaskCreationRequestCommand(RequestId, "Out of scope"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        requests.Verify(x => x.Update(It.Is<TaskCreationRequest>(r =>
            r.Status == TaskCreationRequestStatuses.Rejected && r.DecisionComment == "Out of scope")), Times.Once);
    }

    [Fact]
    public async Task Handle_NonOwner_ReturnsForbidden()
    {
        var (handler, requests) = Build(OtherEmployeeId);
        var result = await handler.Handle(new RejectTaskCreationRequestCommand(RequestId, "No"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        requests.Verify(x => x.Update(It.IsAny<TaskCreationRequest>()), Times.Never);
    }
}

public class CancelTaskCreationRequestCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid RequesterEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid RequestId = Guid.NewGuid();

    private (CancelTaskCreationRequestCommandHandler Handler, Mock<ITaskCreationRequestRepository> Requests) Build(Guid callerEmployeeId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerEmployeeId);

        var pending = new TaskCreationRequest
        {
            Id = RequestId, TenantId = TenantId, ObjectiveId = Guid.NewGuid(), RequestedByEmployeeId = RequesterEmployeeId,
            PayloadJson = "{}", Status = TaskCreationRequestStatuses.Pending, CreatedById = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
        };
        var requests = new Mock<ITaskCreationRequestRepository>();
        requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, RequestId, It.IsAny<CancellationToken>())).ReturnsAsync(pending);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> op, CancellationToken ct) => op(ct));

        var handler = new CancelTaskCreationRequestCommandHandler(currentUser.Object, identity.Object, requests.Object, unitOfWork.Object);
        return (handler, requests);
    }

    [Fact]
    public async Task Handle_Requester_CancelsPendingRequest()
    {
        var (handler, requests) = Build(RequesterEmployeeId);
        var result = await handler.Handle(new CancelTaskCreationRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        requests.Verify(x => x.Update(It.Is<TaskCreationRequest>(r => r.Status == TaskCreationRequestStatuses.Cancelled)), Times.Once);
    }

    [Fact]
    public async Task Handle_NonRequester_ReturnsForbidden()
    {
        var (handler, requests) = Build(OtherEmployeeId);
        var result = await handler.Handle(new CancelTaskCreationRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        requests.Verify(x => x.Update(It.IsAny<TaskCreationRequest>()), Times.Never);
    }
}
