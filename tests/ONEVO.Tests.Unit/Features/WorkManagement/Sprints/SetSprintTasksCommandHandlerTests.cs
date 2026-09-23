using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.SetSprintTasks;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class SetSprintTasksCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();

    private Sprint _sprint = null!;
    private Mock<ISprintRepository> _sprints = null!;
    private Mock<ISprintTaskAssignmentService> _assignment = null!;
    private Mock<ISprintAccessService> _access = null!;
    private Mock<ISprintActivityLogRepository> _logs = null!;
    private Mock<IUnitOfWork> _unitOfWork = null!;

    private SetSprintTasksCommandHandler Build(bool sprintFound = true)
    {
        _sprint = new Sprint
        {
            Id = SprintId, TenantId = TenantId, ProjectId = ProjectId,
            Name = "Sprint 1", Status = SprintStatuses.Active, CreatedById = UserId, CreatedAt = DateTimeOffset.UtcNow
        };

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeId);

        _sprints = new Mock<ISprintRepository>();
        _sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sprintFound ? _sprint : null);

        _assignment = new Mock<ISprintTaskAssignmentService>();
        _assignment.Setup(x => x.PrepareAsync(TenantId, _sprint, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(), EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SprintTaskChangeSet>.Success(SprintTaskChangeSet.Empty));

        _access = new Mock<ISprintAccessService>();
        _access.Setup(x => x.CanManageAsync(TenantId, _sprint, UserId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _logs = new Mock<ISprintActivityLogRepository>();

        _unitOfWork = new Mock<IUnitOfWork>();
        _unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<SprintResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<SprintResponse>>> op, CancellationToken ct) => op(ct));
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return new SetSprintTasksCommandHandler(
            currentUser.Object, identity.Object, _sprints.Object, _assignment.Object, _access.Object, _logs.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task Handle_SprintNotFound_ReturnsNotFound()
    {
        var handler = Build(sprintFound: false);

        var result = await handler.Handle(new SetSprintTasksCommand(SprintId, new[] { Guid.NewGuid() }, Array.Empty<Guid>()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_PrepareForbidden_ReturnsForbiddenAndDoesNotSave()
    {
        var handler = Build();
        _assignment.Setup(x => x.PrepareAsync(TenantId, _sprint, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(), EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SprintTaskChangeSet>.Forbidden("You can only add tasks from modules you own."));

        var result = await handler.Handle(new SetSprintTasksCommand(SprintId, new[] { Guid.NewGuid() }, Array.Empty<Guid>()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AddAndRemove_LogsBoth()
    {
        var handler = Build();
        var added = new WorkTask { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId };
        var removed = new WorkTask { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId };
        var changes = new SprintTaskChangeSet(new[] { added }, new[] { removed });
        _assignment.Setup(x => x.PrepareAsync(TenantId, _sprint, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(), EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SprintTaskChangeSet>.Success(changes));

        var result = await handler.Handle(new SetSprintTasksCommand(SprintId, new[] { added.Id }, new[] { removed.Id }), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _assignment.Verify(x => x.Apply(changes, SprintId), Times.Once);
        _logs.Verify(x => x.AddAsync(It.Is<SprintActivityLog>(l => l.Action == SprintActivityActions.TasksAdded), It.IsAny<CancellationToken>()), Times.Once);
        _logs.Verify(x => x.AddAsync(It.Is<SprintActivityLog>(l => l.Action == SprintActivityActions.TasksRemoved), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EmptyChangeSet_SucceedsWithNoLogsOrSave()
    {
        var handler = Build();

        var result = await handler.Handle(new SetSprintTasksCommand(SprintId, Array.Empty<Guid>(), Array.Empty<Guid>()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _logs.Verify(x => x.AddAsync(It.IsAny<SprintActivityLog>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
