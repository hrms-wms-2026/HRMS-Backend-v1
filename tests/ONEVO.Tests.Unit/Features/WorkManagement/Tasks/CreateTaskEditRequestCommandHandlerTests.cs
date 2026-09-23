using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskEditRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class CreateTaskEditRequestCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid MemberEmployeeId = Guid.NewGuid();
    private static readonly Guid OutsiderEmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();

    private (CreateTaskEditRequestCommandHandler Handler, Mock<ITaskEditRequestRepository> Requests) Build(
        Guid callerEmployeeId, bool callerIsMember, string sprintStatus = SprintStatuses.Active)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(callerEmployeeId);
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [callerEmployeeId] = "Test Member" });

        var task = new WorkTask { Id = TaskId, TenantId = TenantId, ObjectiveId = ObjectiveId, SprintId = SprintId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow };
        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var sprint = new Sprint { Id = SprintId, TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "S1", Status = sprintStatus, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 14), CreatedAt = DateTimeOffset.UtcNow };
        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsActiveMemberAsync(TenantId, ObjectiveId, callerEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(callerIsMember);

        var requests = new Mock<ITaskEditRequestRepository>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<TaskEditRequestResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<TaskEditRequestResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateTaskEditRequestCommandHandler(
            currentUser.Object, identity.Object, tasks.Object, objectives.Object, sprints.Object, membership.Object, requests.Object, unitOfWork.Object);
        return (handler, requests);
    }

    [Fact]
    public async Task Handle_ActiveMember_CreatesRequestWithResolvedName()
    {
        var (handler, requests) = Build(MemberEmployeeId, callerIsMember: true);
        var command = new CreateTaskEditRequestCommand(TaskId, "New title", null, "high", null, null, null, null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Test Member", result.Value!.RequestedByName);
        requests.Verify(x => x.AddAsync(It.Is<TaskEditRequest>(r => r.TaskId == TaskId && r.RequestedByEmployeeId == MemberEmployeeId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Owner_ReturnsFailure_NoRequestNeeded()
    {
        var (handler, requests) = Build(OwnerEmployeeId, callerIsMember: true);
        var command = new CreateTaskEditRequestCommand(TaskId, "New title", null, "high", null, null, null, null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        requests.Verify(x => x.AddAsync(It.IsAny<TaskEditRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NotAMember_ReturnsForbidden()
    {
        var (handler, requests) = Build(OutsiderEmployeeId, callerIsMember: false);
        var command = new CreateTaskEditRequestCommand(TaskId, "New title", null, "high", null, null, null, null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_SprintAchieved_ReturnsForbidden()
    {
        var (handler, requests) = Build(MemberEmployeeId, callerIsMember: true, sprintStatus: SprintStatuses.Achieved);
        var command = new CreateTaskEditRequestCommand(TaskId, "New title", null, "high", null, null, null, null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        requests.Verify(x => x.AddAsync(It.IsAny<TaskEditRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
