using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.EditObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class EditObjectiveCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid HeadId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();

    private static EditObjectiveCommand ValidCommand(DateOnly? endDate = null, decimal allocatedHours = 15m) => new(
        ObjectiveId, "Updated Title", "updated desc", new DateOnly(2026, 2, 1), endDate ?? new DateOnly(2026, 4, 1), allocatedHours);

    private static Objective ParentObjective() => new()
    {
        Id = ParentId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, Title = "Parent",
        OwnerId = HeadId, IsActive = true, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1),
        AllocatedHours = 40m, CreatedAt = DateTimeOffset.UtcNow
    };

    private static Objective SubObjective(Guid createdById, bool isDefault = false, bool isActive = true) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = ParentId, IsDefault = isDefault,
        Title = "Sub", OwnerId = HeadId, ReportingManagerId = createdById, CreatedById = createdById, IsActive = isActive,
        StartDate = new DateOnly(2026, 1, 15), EndDate = new DateOnly(2026, 5, 1), AllocatedHours = 20m,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private (EditObjectiveCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IObjectiveChangeRequestRepository> Requests) BuildHandler(
        Objective? objective, Objective? parent, bool hasPending = false, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? HeadId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ParentId, It.IsAny<CancellationToken>())).ReturnsAsync(parent);

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        requests.Setup(x => x.HasPendingForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(hasPending);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new EditObjectiveCommandHandler(currentUser.Object, objectives.Object, requests.Object, unitOfWork.Object);
        return (handler, objectives, requests);
    }

    [Fact]
    public async Task Handle_NonConflictingEditByHead_AppliesImmediately()
    {
        var (handler, objectives, requests) = BuildHandler(SubObjective(createdById: OtherUserId), ParentObjective());

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Applied);
        Assert.Equal("Updated Title", result.Value.Objective!.Title);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Once);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ConflictingEditByCreator_AppliesImmediately()
    {
        var (handler, objectives, requests) = BuildHandler(SubObjective(createdById: HeadId), ParentObjective());
        var command = ValidCommand(endDate: new DateOnly(2026, 7, 1));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Applied);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Once);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ConflictingEditByNonCreatorHead_CreatesPendingRequestInsteadOfApplying()
    {
        var (handler, objectives, requests) = BuildHandler(SubObjective(createdById: OtherUserId), ParentObjective());
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
    public async Task Handle_ConflictingEditWithAlreadyPendingRequest_ReturnsConflict()
    {
        var (handler, _, _) = BuildHandler(SubObjective(createdById: OtherUserId), ParentObjective(), hasPending: true);
        var command = ValidCommand(endDate: new DateOnly(2026, 7, 1));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var (handler, _, _) = BuildHandler(SubObjective(createdById: OtherUserId), ParentObjective(), callerId: OtherUserId);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DefaultObjective_ReturnsBadRequest()
    {
        var (handler, _, _) = BuildHandler(SubObjective(createdById: HeadId, isDefault: true), ParentObjective());

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveNotFound_ReturnsNotFound()
    {
        var (handler, _, _) = BuildHandler(null, ParentObjective());

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveInactive_ReturnsNotFound()
    {
        var (handler, _, _) = BuildHandler(SubObjective(createdById: OtherUserId, isActive: false), ParentObjective());

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NonConflictingEditWithAlreadyPendingRequest_ReturnsConflict()
    {
        var (handler, objectives, requests) = BuildHandler(SubObjective(createdById: OtherUserId), ParentObjective(), hasPending: true);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Never);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
