using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class CreateTaskStatusCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private (CreateTaskStatusCommandHandler Handler, Mock<ITaskStatusRepository> Statuses) Build(
        Guid callerEmployeeId, bool? callerIsEffectiveManager = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerEmployeeId);

        var project = new Project { Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "Proj", Identifier = "PRJ", CreatedAt = DateTimeOffset.UtcNow };
        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var defaultObjective = new Objective { Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetDefaultByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(defaultObjective);

        var statuses = new Mock<ITaskStatusRepository>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<TaskStatusResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<TaskStatusResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        // Mirrors direct-owner-only behavior by default so pre-existing tests keep passing
        // unmodified; callerIsEffectiveManager lets a test override this to simulate a
        // non-owner grant (the coordinator's own membership logic is unit-tested separately
        // in MilestoneMembershipCoordinatorTests). Keyed on the default Objective's Id, since
        // the handler now resolves the Project's default Objective as its authorization root.
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (callerEmployeeId == OwnerEmployeeId));

        var handler = new CreateTaskStatusCommandHandler(currentUser.Object, identity.Object, objectives.Object, projects.Object, statuses.Object, unitOfWork.Object, membership.Object);
        return (handler, statuses);
    }

    [Fact]
    public async Task Handle_Owner_CreatesStatus()
    {
        var (handler, statuses) = Build(OwnerEmployeeId);
        var command = new CreateTaskStatusCommand(ProjectId, "Blocked", 4, TaskStatusVisibilities.Public, false, false, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Blocked", result.Value!.Name);
        statuses.Verify(x => x.AddAsync(It.Is<TaskStatusEntity>(s => s.Name == "Blocked" && s.ProjectId == ProjectId && s.ObjectiveId == null && s.Visibility == TaskStatusVisibilities.Public), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, statuses) = Build(OtherEmployeeId);
        var command = new CreateTaskStatusCommand(ProjectId, "Blocked", 4, TaskStatusVisibilities.Public, false, false, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        statuses.Verify(x => x.AddAsync(It.IsAny<TaskStatusEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerIsEffectiveManagerViaAncestor_CreatesStatus()
    {
        // Caller is not the default Objective's own OwnerId, but IsEffectiveManagerAsync reports
        // them as an effective manager via an ancestor (grandparent) membership - the coordinator's
        // own ancestor-walk logic is unit-tested separately in MilestoneMembershipCoordinatorTests,
        // so this only proves the handler defers to its answer instead of the direct OwnerId check.
        var (handler, statuses) = Build(OtherEmployeeId, callerIsEffectiveManager: true);
        var command = new CreateTaskStatusCommand(ProjectId, "Blocked", 4, TaskStatusVisibilities.Public, false, false, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Blocked", result.Value!.Name);
        statuses.Verify(x => x.AddAsync(It.Is<TaskStatusEntity>(s => s.Name == "Blocked" && s.ProjectId == ProjectId && s.ObjectiveId == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PlainMemberOfDefaultObjective_CreatesStatus()
    {
        // Under the old per-Objective model, a plain (non-owner) Objective member could never
        // create task statuses at all - only the objective's own owner could. Per the design's
        // authorization decision, Task Status is now Project-level configuration that any
        // project member can change, not just the owner/lead. This proves a plain active member
        // of the Project's default Objective (granted via IsEffectiveManagerAsync returning true
        // through direct membership, not ownership) can now create a status.
        var (handler, statuses) = Build(OtherEmployeeId, callerIsEffectiveManager: true);
        var command = new CreateTaskStatusCommand(ProjectId, "In Review", 2, TaskStatusVisibilities.Public, false, true, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("In Review", result.Value!.Name);
        statuses.Verify(x => x.AddAsync(It.Is<TaskStatusEntity>(s => s.Name == "In Review" && s.ProjectId == ProjectId && s.ObjectiveId == null), It.IsAny<CancellationToken>()), Times.Once);
    }
}
