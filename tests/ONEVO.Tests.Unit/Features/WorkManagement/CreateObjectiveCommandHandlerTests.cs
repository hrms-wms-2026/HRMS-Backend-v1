using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.CreateObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class CreateObjectiveCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();

    private static CreateObjectiveCommand ValidCommand(Guid? headUserId = null) => new(
        ParentId, "Sub Milestone", "desc", new DateOnly(2026, 2, 1), new DateOnly(2026, 5, 1), 20m, headUserId);

    private static Objective ParentObjective(Guid ownerId, bool isActive = true) => new()
    {
        Id = ParentId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, Title = "Parent",
        OwnerId = ownerId, IsActive = isActive, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1),
        AllocatedHours = 40m, CreatedAt = DateTimeOffset.UtcNow
    };

    private (CreateObjectiveCommandHandler Handler, Mock<IObjectiveRepository> Objectives) BuildHandler(Objective? parent)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ParentId, It.IsAny<CancellationToken>())).ReturnsAsync(parent);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateObjectiveCommandHandler(currentUser.Object, objectives.Object, unitOfWork.Object);
        return (handler, objectives);
    }

    [Fact]
    public async Task Handle_CallerIsParentHead_CreatesWithSelfAsDefaultHeadAndReportingManager()
    {
        var (handler, objectives) = BuildHandler(ParentObjective(ownerId: UserId));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserId, result.Value!.OwnerId);
        Assert.Equal(UserId, result.Value.ReportingManagerId);
        objectives.Verify(x => x.AddAsync(It.Is<Objective>(o => o.OwnerId == UserId && o.ReportingManagerId == UserId && o.ParentObjectiveId == ParentId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExplicitHeadUserId_ReportingManagerStaysCreatorNotTheAssignedHead()
    {
        var (handler, objectives) = BuildHandler(ParentObjective(ownerId: UserId));

        var result = await handler.Handle(ValidCommand(headUserId: OtherUserId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OtherUserId, result.Value!.OwnerId);
        Assert.Equal(UserId, result.Value.ReportingManagerId);
    }

    [Fact]
    public async Task Handle_CallerNotParentHead_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(ParentObjective(ownerId: OtherUserId));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ParentNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_InactiveParent_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(ParentObjective(ownerId: UserId, isActive: false));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DatesOutsideParentRange_ReturnsBadRequest()
    {
        var (handler, _) = BuildHandler(ParentObjective(ownerId: UserId));
        var command = ValidCommand() with { EndDate = new DateOnly(2026, 7, 1) };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_HoursExceedParentTotal_ReturnsBadRequest()
    {
        var (handler, _) = BuildHandler(ParentObjective(ownerId: UserId));
        var command = ValidCommand() with { AllocatedHours = 999m };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }
}
