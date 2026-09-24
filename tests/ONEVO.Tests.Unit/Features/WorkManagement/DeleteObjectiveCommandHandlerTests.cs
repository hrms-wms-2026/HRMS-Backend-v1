using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.DeleteObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class DeleteObjectiveCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid HeadUserId = Guid.NewGuid();
    private static readonly Guid HeadEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private static Objective SubObjective(Guid createdById, bool isDefault = false, bool isActive = true) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, IsDefault = isDefault, Title = "Sub",
        OwnerId = HeadEmployeeId, ReportingManagerId = createdById, CreatedById = createdById, IsActive = isActive,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (DeleteObjectiveCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IObjectiveChangeRequestRepository> Requests) BuildHandler(
        Objective? objective, bool hasPending = false, Guid? callerId = null, bool? callerIsEffectiveManager = null)
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

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        requests.Setup(x => x.HasPendingForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(hasPending);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        // Mirrors direct-owner-only behavior by default so pre-existing tests keep passing
        // unmodified; callerIsEffectiveManager lets a test override this to simulate an
        // ancestor-cascade grant (the coordinator's own ancestor-walk logic is unit-tested
        // separately in MilestoneMembershipCoordinatorTests).
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (objective is not null && objective.OwnerId == resolvedCallerEmployeeId));

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new DeleteObjectiveCommandHandler(currentUser.Object, identity.Object, objectives.Object, requests.Object, unitOfWork.Object, membership.Object);
        return (handler, objectives, requests);
    }

    [Fact]
    public async Task Handle_CreatorHeadDeletes_AppliesImmediately()
    {
        var (handler, objectives, requests) = BuildHandler(SubObjective(createdById: HeadUserId));

        var result = await handler.Handle(new DeleteObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Applied);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => !o.IsActive)), Times.Once);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NonCreatorHeadDeletes_CreatesPendingRequest()
    {
        var (handler, objectives, requests) = BuildHandler(SubObjective(createdById: OtherUserId));

        var result = await handler.Handle(new DeleteObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        Assert.Equal(OtherUserId, result.Value.PendingRequest!.ReportingManagerId);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Never);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyPendingRequest_ReturnsConflict()
    {
        var (handler, _, _) = BuildHandler(SubObjective(createdById: OtherUserId), hasPending: true);

        var result = await handler.Handle(new DeleteObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var (handler, _, _) = BuildHandler(SubObjective(createdById: OtherUserId), callerId: OtherUserId);

        var result = await handler.Handle(new DeleteObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerIsActiveMemberOfAncestorObjective_AppliesImmediately()
    {
        // Caller is not this objective's own OwnerId, but IsEffectiveManagerAsync reports them as
        // an effective manager via an ancestor (grandparent) membership - the coordinator's own
        // ancestor-walk logic is unit-tested separately in MilestoneMembershipCoordinatorTests, so
        // this only proves the handler defers to its answer instead of the direct OwnerId check.
        var (handler, objectives, _) = BuildHandler(
            SubObjective(createdById: OtherUserId), callerId: OtherUserId, callerIsEffectiveManager: true);

        var result = await handler.Handle(new DeleteObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Applied);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => !o.IsActive)), Times.Once);
    }

    [Fact]
    public async Task Handle_DefaultObjective_ReturnsBadRequest()
    {
        var (handler, _, _) = BuildHandler(SubObjective(createdById: HeadUserId, isDefault: true));

        var result = await handler.Handle(new DeleteObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AlreadyDeleted_ReturnsConflict()
    {
        var (handler, objectives, requests) = BuildHandler(SubObjective(createdById: HeadUserId, isActive: false));

        var result = await handler.Handle(new DeleteObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Never);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        var (handler, _, _) = BuildHandler(null);

        var result = await handler.Handle(new DeleteObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
