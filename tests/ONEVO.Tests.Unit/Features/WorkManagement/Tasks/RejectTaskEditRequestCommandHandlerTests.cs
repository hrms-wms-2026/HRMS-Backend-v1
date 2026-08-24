using System.Text.Json;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.RejectTaskEditRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class RejectTaskEditRequestCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid RequesterEmployeeId = Guid.NewGuid();
    private static readonly Guid RequesterUserId = Guid.NewGuid();
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private (
        RejectTaskEditRequestCommandHandler Handler,
        Mock<ITaskEditRequestRepository> Requests,
        Mock<INotificationDispatcher> Notifications) Build(Guid callerEmployeeId, bool? callerIsEffectiveManager = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerEmployeeId);

        var payload = new TaskEditRequestPayload(
            "Updated task", null, WorkTaskPriorities.High, null, null, null);
        var pending = new TaskEditRequest
        {
            Id = RequestId,
            TenantId = TenantId,
            TaskId = TaskId,
            RequestedByEmployeeId = RequesterEmployeeId,
            PayloadJson = JsonSerializer.Serialize(payload),
            Status = TaskEditRequestStatuses.Pending,
            CreatedById = UserId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var requests = new Mock<ITaskEditRequestRepository>();
        requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, RequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkTask
            {
                Id = TaskId,
                TenantId = TenantId,
                ObjectiveId = ObjectiveId,
                Title = "Original task",
                CreatedAt = DateTimeOffset.UtcNow
            });

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Objective
            {
                Id = ObjectiveId,
                TenantId = TenantId,
                OwnerId = OwnerEmployeeId,
                Title = "Objective",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(
                TenantId, RequesterEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee
            {
                Id = RequesterEmployeeId,
                TenantId = TenantId,
                UserId = RequesterUserId
            });
        // Mirrors direct-owner-only behavior by default so pre-existing tests keep passing
        // unmodified; callerIsEffectiveManager lets a test override this to simulate an
        // ancestor-cascade grant (the coordinator's own ancestor-walk logic is unit-tested
        // separately in MilestoneMembershipCoordinatorTests).
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (callerEmployeeId == OwnerEmployeeId));

        var notifications = new Mock<INotificationDispatcher>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new RejectTaskEditRequestCommandHandler(
            currentUser.Object,
            identity.Object,
            requests.Object,
            tasks.Object,
            objectives.Object,
            membership.Object,
            notifications.Object,
            unitOfWork.Object);

        return (handler, requests, notifications);
    }

    [Fact]
    public async Task Handle_Owner_RejectsWithCommentAndNotifiesRequester()
    {
        var (handler, requests, notifications) = Build(OwnerEmployeeId);

        var result = await handler.Handle(
            new RejectTaskEditRequestCommand(RequestId, " Out of scope "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        requests.Verify(x => x.Update(It.Is<TaskEditRequest>(r =>
            r.Status == TaskEditRequestStatuses.Rejected
            && r.DecisionComment == "Out of scope"
            && r.DecidedByEmployeeId == OwnerEmployeeId
            && r.DecidedAt.HasValue)), Times.Once);
        notifications.Verify(x => x.SendTemplatedAsync(
            TenantId,
            RequesterUserId,
            "work_task_edit_request_decided",
            It.Is<IReadOnlyDictionary<string, string>>(p =>
                p["decision"] == "rejected"
                && p["taskTitle"] == "Updated task"
                && p["objectiveName"] == "Objective"),
            "task_edit_request",
            RequestId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NonOwner_ReturnsForbiddenWithoutUpdatingOrNotifying()
    {
        var (handler, requests, notifications) = Build(OtherEmployeeId);

        var result = await handler.Handle(
            new RejectTaskEditRequestCommand(RequestId, "No"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        requests.Verify(x => x.Update(It.IsAny<TaskEditRequest>()), Times.Never);
        notifications.Verify(x => x.SendTemplatedAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(),
            It.IsAny<string?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerIsEffectiveManagerViaAncestor_RejectsWithCommentAndNotifiesRequester()
    {
        // Caller is not this objective's own OwnerId, but IsEffectiveManagerAsync reports them as
        // an effective manager via an ancestor (grandparent) membership - the coordinator's own
        // ancestor-walk logic is unit-tested separately in MilestoneMembershipCoordinatorTests, so
        // this only proves the handler defers to its answer instead of the direct OwnerId check.
        var (handler, requests, notifications) = Build(OtherEmployeeId, callerIsEffectiveManager: true);

        var result = await handler.Handle(
            new RejectTaskEditRequestCommand(RequestId, " Out of scope "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        requests.Verify(x => x.Update(It.Is<TaskEditRequest>(r =>
            r.Status == TaskEditRequestStatuses.Rejected
            && r.DecisionComment == "Out of scope"
            && r.DecidedByEmployeeId == OtherEmployeeId
            && r.DecidedAt.HasValue)), Times.Once);
        notifications.Verify(x => x.SendTemplatedAsync(
            TenantId,
            RequesterUserId,
            "work_task_edit_request_decided",
            It.IsAny<IReadOnlyDictionary<string, string>>(),
            "task_edit_request",
            RequestId,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
