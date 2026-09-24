using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CreateSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class CreateSprintCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private Mock<IProjectRepository> _projects = null!;
    private Mock<IProjectMemberRepository> _members = null!;
    private Mock<IPermissionResolver> _permissionResolver = null!;
    private Mock<ISprintRepository> _sprints = null!;
    private Mock<ISprintTaskAssignmentService> _assignment = null!;
    private Mock<ISprintActivityLogRepository> _logs = null!;

    private CreateSprintCommandHandler Build(
        bool isMember = true,
        bool hasReadPermission = false,
        bool projectFound = true,
        Guid? callerEmployeeId = null)
    {
        var resolvedCallerEmployeeId = callerEmployeeId ?? EmployeeId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedCallerEmployeeId);

        var project = new Project { Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "P1", CreatedAt = DateTimeOffset.UtcNow };
        _projects = new Mock<IProjectRepository>();
        _projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projectFound ? project : null);

        _members = new Mock<IProjectMemberRepository>();
        _members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(isMember);

        _permissionResolver = new Mock<IPermissionResolver>();
        _permissionResolver.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasReadPermission ? new List<string> { "projects:read" } : new List<string>());

        _sprints = new Mock<ISprintRepository>();

        _assignment = new Mock<ISprintTaskAssignmentService>();
        _assignment.Setup(x => x.PrepareAsync(
                TenantId, It.IsAny<Sprint>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SprintTaskChangeSet>.Success(SprintTaskChangeSet.Empty));

        _logs = new Mock<ISprintActivityLogRepository>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<SprintResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<SprintResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return new CreateSprintCommandHandler(
            currentUser.Object, identity.Object, _projects.Object, _members.Object, _permissionResolver.Object,
            _sprints.Object, _assignment.Object, _logs.Object, unitOfWork.Object);
    }

    [Fact]
    public async Task Handle_ActiveMemberNoTasks_CreatesDraftSprintAndLogsCreated()
    {
        var handler = Build();

        var result = await handler.Handle(new CreateSprintCommand(ProjectId, "Sprint 1", null, Array.Empty<Guid>()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProjectId, result.Value!.ProjectId);
        Assert.Equal(SprintStatuses.Draft, result.Value!.Status);
        _sprints.Verify(x => x.AddAsync(
            It.Is<Sprint>(s => s.ProjectId == ProjectId && s.CreatedById == UserId && s.Status == SprintStatuses.Draft),
            It.IsAny<CancellationToken>()), Times.Once);
        _logs.Verify(x => x.AddAsync(
            It.Is<SprintActivityLog>(l => l.Action == SprintActivityActions.Created), It.IsAny<CancellationToken>()), Times.Once);
        _assignment.Verify(x => x.PrepareAsync(
            TenantId, It.IsAny<Sprint>(), It.Is<IReadOnlyCollection<Guid>>(c => c.Count == 0), It.Is<IReadOnlyCollection<Guid>>(c => c.Count == 0),
            EmployeeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithTasks_AssignsAndLogsTasksAdded()
    {
        var handler = Build();
        var taskIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var changes = new SprintTaskChangeSet(
            taskIds.Select(id => new WorkTask { Id = id, TenantId = TenantId, ProjectId = ProjectId }).ToList(),
            Array.Empty<WorkTask>());
        _assignment.Setup(x => x.PrepareAsync(TenantId, It.IsAny<Sprint>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(), EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SprintTaskChangeSet>.Success(changes));

        var result = await handler.Handle(new CreateSprintCommand(ProjectId, "Sprint 1", null, taskIds), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _assignment.Verify(x => x.Apply(changes, result.Value!.Id), Times.Once);
        _logs.Verify(x => x.AddAsync(It.Is<SprintActivityLog>(l => l.Action == SprintActivityActions.TasksAdded && l.DetailsJson!.Contains(taskIds[0].ToString())), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TaskMovedFromAnotherSprint_LogsTasksRemovedOnSourceSprint()
    {
        var handler = Build();
        var sourceSprintId = Guid.NewGuid();
        var moved = new WorkTask { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, SprintId = sourceSprintId };
        var changes = new SprintTaskChangeSet(new[] { moved }, Array.Empty<WorkTask>());
        _assignment.Setup(x => x.PrepareAsync(TenantId, It.IsAny<Sprint>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(), EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SprintTaskChangeSet>.Success(changes));

        var result = await handler.Handle(new CreateSprintCommand(ProjectId, "Sprint 1", null, new List<Guid> { moved.Id }), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _logs.Verify(x => x.AddAsync(It.Is<SprintActivityLog>(l =>
                l.SprintId == sourceSprintId && l.Action == SprintActivityActions.TasksRemoved &&
                l.DetailsJson!.Contains(moved.Id.ToString())),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PrepareAsyncForbidden_ReturnsForbiddenAndDoesNotCreate()
    {
        var handler = Build();
        _assignment.Setup(x => x.PrepareAsync(TenantId, It.IsAny<Sprint>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(), EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SprintTaskChangeSet>.Forbidden("You can only add tasks from modules you own."));

        var result = await handler.Handle(new CreateSprintCommand(ProjectId, "Sprint 1", null, new List<Guid> { Guid.NewGuid() }), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        _sprints.Verify(x => x.AddAsync(It.IsAny<Sprint>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NotMemberNoReadPermission_ReturnsForbidden()
    {
        var handler = Build(isMember: false, hasReadPermission: false);

        var result = await handler.Handle(new CreateSprintCommand(ProjectId, "Sprint 1", null, Array.Empty<Guid>()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        _sprints.Verify(x => x.AddAsync(It.IsAny<Sprint>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_HasReadPermissionNotMember_Succeeds()
    {
        var handler = Build(isMember: false, hasReadPermission: true);

        var result = await handler.Handle(new CreateSprintCommand(ProjectId, "Sprint 1", null, Array.Empty<Guid>()), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_ProjectNotFound_ReturnsNotFound()
    {
        var handler = Build(projectFound: false);

        var result = await handler.Handle(new CreateSprintCommand(ProjectId, "Sprint 1", null, Array.Empty<Guid>()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
