using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskCreationRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using System.Text.Json;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class CreateTaskCreationRequestCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();

    private (CreateTaskCreationRequestCommandHandler Handler, Mock<ITaskCreationRequestRepository> Requests) Build(bool isActiveMember)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(EmployeeId);
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [EmployeeId] = "Requester" });

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerId, Title = "Milestone", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsActiveMemberAsync(TenantId, ObjectiveId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(isActiveMember);

        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sprint
            {
                Id = SprintId, TenantId = TenantId, ObjectiveId = ObjectiveId,
                Name = "Sprint 1", Status = SprintStatuses.Active, CreatedAt = DateTimeOffset.UtcNow
            });

        var requests = new Mock<ITaskCreationRequestRepository>();
        var notifications = new Mock<INotificationDispatcher>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<TaskCreationRequestResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<TaskCreationRequestResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new CreateTaskCreationRequestCommandHandler(
            currentUser.Object, identity.Object, objectives.Object, membership.Object, sprints.Object, requests.Object,
            notifications.Object, unitOfWork.Object);
        return (handler, requests);
    }

    [Fact]
    public async Task Handle_ActiveMember_CreatesPendingRequest()
    {
        var (handler, requests) = Build(isActiveMember: true);
        var command = new CreateTaskCreationRequestCommand(ObjectiveId, "New task", null, "task", "medium", null, 5m, null, SprintId);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintId, result.Value!.Payload.SprintId);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.Tasks.Entities.TaskCreationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotActiveMember_ReturnsForbidden()
    {
        var (handler, requests) = Build(isActiveMember: false);
        var command = new CreateTaskCreationRequestCommand(ObjectiveId, "New task", null, "task", "medium", null, 5m, null, SprintId);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.Tasks.Entities.TaskCreationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NullSprintId_StoresNullSprintInPayload()
    {
        var (handler, requests) = Build(isActiveMember: true);
        var command = new CreateTaskCreationRequestCommand(ObjectiveId, "Direct task", null, "task", "medium", null, 5m, null, SprintId: null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Payload.SprintId);
        requests.Verify(x => x.AddAsync(It.Is<TaskCreationRequest>(r =>
            JsonSerializer.Deserialize<TaskCreationRequestPayload>(r.PayloadJson)!.SprintId == null), It.IsAny<CancellationToken>()), Times.Once);
    }
}
