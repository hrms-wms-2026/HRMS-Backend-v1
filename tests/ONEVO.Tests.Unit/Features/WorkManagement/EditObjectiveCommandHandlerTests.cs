using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.EditObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class EditObjectiveCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid HeadUserId = Guid.NewGuid();
    private static readonly Guid HeadEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();

    private static EditObjectiveCommand ValidCommand(DateOnly? endDate = null, decimal allocatedHours = 15m) => new(
        ObjectiveId, "Updated Title", "updated desc", new DateOnly(2026, 2, 1), endDate ?? new DateOnly(2026, 4, 1), allocatedHours);

    private static Objective SubObjective(Guid createdById, bool isDefault = false, bool isActive = true) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = ParentId, IsDefault = isDefault,
        Title = "Sub", OwnerId = HeadEmployeeId, ReportingManagerId = createdById, CreatedById = createdById, IsActive = isActive,
        StartDate = new DateOnly(2026, 1, 15), EndDate = new DateOnly(2026, 5, 1), AllocatedHours = 20m,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private (EditObjectiveCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IObjectiveChangeRequestRepository> Requests, Mock<IMilestoneMembershipCoordinator> Membership) BuildHandler(
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
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [resolvedCallerEmployeeId] = "Requester" });

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        requests.Setup(x => x.HasPendingForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(hasPending);

        // Mirrors direct-owner-only behavior by default so pre-existing tests keep passing
        // unmodified; callerIsEffectiveManager lets a test override this to simulate an
        // ancestor-cascade grant (caller isn't objective.OwnerId but IsEffectiveManagerAsync
        // is still true because they own/are a member of an ancestor - already unit-tested at
        // the coordinator level in MilestoneMembershipCoordinatorTests).
        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (objective is not null && objective.OwnerId == resolvedCallerEmployeeId));
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, EmploymentStatusId = EmploymentStatusIds.Active });

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<ObjectiveEditOutcomeResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<Result<ObjectiveEditOutcomeResponse>>>, CancellationToken>((operation, innerCt) => operation(innerCt));

        var handler = new EditObjectiveCommandHandler(
            currentUser.Object, identity.Object, objectives.Object, requests.Object, unitOfWork.Object, membership.Object,
            new Mock<INotificationDispatcher>().Object);
        return (handler, objectives, requests, membership);
    }

    [Fact]
    public async Task Handle_NonConflictingEditByHead_AlwaysCreatesPendingRequestForApproval()
    {
        var (handler, objectives, requests, _) = BuildHandler(SubObjective(createdById: OtherUserId));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        Assert.NotNull(result.Value.PendingRequest);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Never);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EditByCreator_StillCreatesPendingRequest_CreatorNoLongerBypassesApproval()
    {
        var (handler, objectives, requests, _) = BuildHandler(SubObjective(createdById: HeadUserId));
        var command = ValidCommand(endDate: new DateOnly(2026, 7, 1));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        Assert.NotNull(result.Value.PendingRequest);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Never);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ConflictingEditByNonCreatorHead_CreatesPendingRequestRoutedToReportingManager()
    {
        var (handler, objectives, requests, _) = BuildHandler(SubObjective(createdById: OtherUserId));
        var command = ValidCommand(endDate: new DateOnly(2026, 7, 1));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        Assert.NotNull(result.Value.PendingRequest);
        Assert.Equal(OtherUserId, result.Value.PendingRequest!.ReportingManagerId);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Never);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyPendingRequest_ReturnsConflict()
    {
        var (handler, _, _, _) = BuildHandler(SubObjective(createdById: OtherUserId), hasPending: true);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var (handler, _, _, _) = BuildHandler(SubObjective(createdById: OtherUserId), callerId: OtherUserId);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerIsActiveMemberOfAncestorObjective_CreatesPendingRequest()
    {
        // Caller is not this objective's own OwnerId, but IsEffectiveManagerAsync reports them as
        // an effective manager via an ancestor (grandparent) membership - the coordinator's own
        // ancestor-walk logic is unit-tested separately in MilestoneMembershipCoordinatorTests, so
        // this only proves the handler defers to its answer instead of the direct OwnerId check.
        var (handler, objectives, requests, _) = BuildHandler(
            SubObjective(createdById: OtherUserId), callerId: OtherUserId, callerIsEffectiveManager: true);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Never);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DefaultObjective_ReturnsBadRequest()
    {
        var (handler, _, _, _) = BuildHandler(SubObjective(createdById: HeadUserId, isDefault: true));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveNotFound_ReturnsNotFound()
    {
        var (handler, _, _, _) = BuildHandler(null);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveInactive_ReturnsNotFound()
    {
        var (handler, _, _, _) = BuildHandler(SubObjective(createdById: OtherUserId, isActive: false));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveAchieved_ReturnsBadRequest()
    {
        var achieved = SubObjective(createdById: OtherUserId);
        achieved.IsAchieved = true;
        var (handler, _, _, _) = BuildHandler(achieved);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }
}
