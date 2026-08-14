using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.RemoveObjectiveMember;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class RemoveObjectiveMemberCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid HeadUserId = Guid.NewGuid();
    private static readonly Guid HeadEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid MemberEmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private static Objective SubObjective(bool isAchieved = false) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = false, Title = "Sub",
        OwnerId = HeadEmployeeId, IsActive = true, IsAchieved = isAchieved,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (RemoveObjectiveMemberCommandHandler Handler, Mock<IMilestoneMembershipCoordinator> Membership) BuildHandler(
        Objective? objective, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? HeadUserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, HeadUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HeadEmployeeId);
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, OtherUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OtherEmployeeId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.DeactivateMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new RemoveObjectiveMemberCommandHandler(currentUser.Object, identity.Object, objectives.Object, membership.Object, unitOfWork.Object);
        return (handler, membership);
    }

    [Fact]
    public async Task Handle_HeadRemovesMember_DeactivatesMembership()
    {
        var (handler, membership) = BuildHandler(SubObjective());

        var result = await handler.Handle(new RemoveObjectiveMemberCommand(ObjectiveId, MemberEmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        membership.Verify(x => x.DeactivateMembershipAsync(TenantId, ProjectId, ObjectiveId, MemberEmployeeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TargetIsCurrentHead_ReturnsBadRequest()
    {
        var (handler, membership) = BuildHandler(SubObjective());

        var result = await handler.Handle(new RemoveObjectiveMemberCommand(ObjectiveId, HeadEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        membership.Verify(x => x.DeactivateMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(SubObjective(), callerId: OtherUserId);

        var result = await handler.Handle(new RemoveObjectiveMemberCommand(ObjectiveId, MemberEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveAchieved_ReturnsBadRequest()
    {
        var (handler, _) = BuildHandler(SubObjective(isAchieved: true));

        var result = await handler.Handle(new RemoveObjectiveMemberCommand(ObjectiveId, MemberEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null);

        var result = await handler.Handle(new RemoveObjectiveMemberCommand(ObjectiveId, MemberEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
