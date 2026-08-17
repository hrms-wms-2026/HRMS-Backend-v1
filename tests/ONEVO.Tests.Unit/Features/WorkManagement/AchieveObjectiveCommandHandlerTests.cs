using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.AchieveObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class AchieveObjectiveCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid HeadUserId = Guid.NewGuid();
    private static readonly Guid HeadEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private static Objective SubObjective(Guid createdById, bool isDefault = false, bool isAchieved = false) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = isDefault, Title = "Sub",
        OwnerId = HeadEmployeeId, ReportingManagerId = createdById, CreatedById = createdById, IsActive = true, IsAchieved = isAchieved,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (AchieveObjectiveCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IObjectiveChangeRequestRepository> Requests, Mock<IMilestoneMembershipCoordinator> Membership) BuildHandler(
        Objective? objective, List<Objective>? unachievedChildren = null, bool hasPending = false, Guid? callerId = null,
        IReadOnlyList<Sprint>? sprints = null)
    {
        var resolvedCallerUserId = callerId ?? HeadUserId;
        var resolvedCallerEmployeeId = resolvedCallerUserId == OtherUserId ? OtherEmployeeId : HeadEmployeeId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(resolvedCallerUserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, resolvedCallerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedCallerEmployeeId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unachievedChildren ?? new List<Objective>());

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        requests.Setup(x => x.HasPendingForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(hasPending);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.HasOtherActiveAccessAsync(TenantId, ProjectId, HeadEmployeeId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var sprintRepo = new Mock<ISprintRepository>();
        sprintRepo.Setup(x => x.GetByObjectiveIdAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sprints ?? new List<Sprint>());

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<ObjectiveChangeOutcomeResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<ObjectiveChangeOutcomeResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new AchieveObjectiveCommandHandler(
            currentUser.Object, identity.Object, objectives.Object, requests.Object, membership.Object, sprintRepo.Object, unitOfWork.Object);
        return (handler, objectives, requests, membership);
    }

    private static Sprint SprintOnObjective(string status) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "S1",
        StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 14),
        Status = status, CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_CreatorHeadAchieves_AppliesImmediately()
    {
        var (handler, objectives, requests, membership) = BuildHandler(SubObjective(createdById: HeadUserId));

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Applied);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => o.IsAchieved && o.AchievedAt != null)), Times.Once);
        membership.Verify(x => x.DeactivateMembershipAsync(TenantId, ProjectId, ObjectiveId, HeadEmployeeId, It.IsAny<CancellationToken>()), Times.Once);
        requests.Verify(x => x.AddAsync(It.IsAny<ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NonCreatorHeadAchieves_CreatesPendingRequest()
    {
        var (handler, objectives, requests, _) = BuildHandler(SubObjective(createdById: OtherUserId));

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Never);
        requests.Verify(x => x.AddAsync(It.Is<ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(
            r => r.RequestType == "achieve"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DirectChildNotAchieved_ReturnsBadRequest()
    {
        var unachievedChild = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, IsAchieved = false, IsActive = true };
        var (handler, _, _, _) = BuildHandler(SubObjective(createdById: HeadUserId), unachievedChildren: new List<Objective> { unachievedChild });

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AlreadyAchieved_ReturnsConflict()
    {
        var (handler, _, _, _) = BuildHandler(SubObjective(createdById: HeadUserId, isAchieved: true));

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var (handler, _, _, _) = BuildHandler(SubObjective(createdById: OtherUserId), callerId: OtherUserId);

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DefaultObjective_ReturnsBadRequest()
    {
        var (handler, _, _, _) = BuildHandler(SubObjective(createdById: HeadUserId, isDefault: true));

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AlreadyPendingRequest_ReturnsConflict()
    {
        var (handler, _, _, _) = BuildHandler(SubObjective(createdById: OtherUserId), hasPending: true);

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        var (handler, _, _, _) = BuildHandler(null);

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_SprintNeitherCompleteNorAchieved_ReturnsFailure()
    {
        var (handler, objectives, _, _) = BuildHandler(
            SubObjective(createdById: HeadUserId),
            sprints: new List<Sprint> { SprintOnObjective(SprintStatuses.Active) });

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AllSprintsCompleteOrAchieved_Succeeds()
    {
        var (handler, objectives, _, _) = BuildHandler(
            SubObjective(createdById: HeadUserId),
            sprints: new List<Sprint>
            {
                SprintOnObjective(SprintStatuses.Complete),
                SprintOnObjective(SprintStatuses.Achieved)
            });

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => o.IsAchieved)), Times.Once);
    }

    [Fact]
    public async Task Handle_ZeroSprints_Succeeds()
    {
        var (handler, objectives, _, _) = BuildHandler(SubObjective(createdById: HeadUserId), sprints: new List<Sprint>());

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => o.IsAchieved)), Times.Once);
    }
}
