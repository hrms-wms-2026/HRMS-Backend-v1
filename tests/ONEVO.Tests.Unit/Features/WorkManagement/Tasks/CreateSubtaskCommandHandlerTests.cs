using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateSubtask;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class CreateSubtaskCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ParentTaskId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid StatusId = Guid.NewGuid();
    private static readonly Guid CallerEmployeeId = Guid.NewGuid();
    private static readonly Guid AssigneeEmployeeId = Guid.NewGuid();
    private static readonly Guid AssigneeUserId = Guid.NewGuid();

    private (CreateSubtaskCommandHandler Handler, Mock<IWorkTaskRepository> Tasks, Mock<ITaskAssignmentRepository> Assignments) Build(
        WorkTask? parent, Employee? assignee, bool callerIsEffectiveManager = true)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerEmployeeId);

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, ParentTaskId, It.IsAny<CancellationToken>())).ReturnsAsync(parent);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = CallerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var project = new Project { Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "P", Identifier = "PRJ", CreatedAt = DateTimeOffset.UtcNow };
        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);
        projects.Setup(x => x.IncrementAndGetNextTaskNumberAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(7);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, CallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager);
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, AssigneeEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignee);

        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus>
            {
                new() { Id = StatusId, TenantId = TenantId, Name = "To Do", Category = TaskStatusCategories.NotStarted, DisplayOrder = 0 }
            });

        var assignments = new Mock<ITaskAssignmentRepository>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses.WorkTaskResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses.WorkTaskResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateSubtaskCommandHandler(
            currentUser.Object, identity.Object, objectives.Object, projects.Object, tasks.Object,
            statuses.Object, assignments.Object, membership.Object, unitOfWork.Object);
        return (handler, tasks, assignments);
    }

    [Fact]
    public async Task Handle_HappyPathWithAssignee_CreatesSubtaskAndAssigns()
    {
        var parent = new WorkTask { Id = ParentTaskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, CategoryId = CategoryId, Title = "Parent", ShortId = "PRJ-1", CreatedAt = DateTimeOffset.UtcNow };
        var assignee = new Employee { Id = AssigneeEmployeeId, TenantId = TenantId, UserId = AssigneeUserId, EmployeeNumber = "E1", HireDate = new DateOnly(2020, 1, 1) };
        var (handler, tasks, assignments) = Build(parent, assignee);

        var result = await handler.Handle(new CreateSubtaskCommand(ParentTaskId, "Do the sub-thing", "high", null, AssigneeEmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ParentTaskId, result.Value!.ParentTaskId);
        Assert.Equal(CategoryId, result.Value.CategoryId);
        tasks.Verify(x => x.AddAsync(It.Is<WorkTask>(t => t.ParentTaskId == ParentTaskId && t.ProjectId == ProjectId && t.ObjectiveId == ObjectiveId && t.CategoryId == CategoryId), It.IsAny<CancellationToken>()), Times.Once);
        assignments.Verify(x => x.AddAsync(It.Is<TaskAssignment>(a => a.EmployeeId == AssigneeEmployeeId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ParentNotFound_ReturnsNotFound()
    {
        var (handler, tasks, _) = Build(parent: null, assignee: null);

        var result = await handler.Handle(new CreateSubtaskCommand(ParentTaskId, "Title", null, null, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        tasks.Verify(x => x.AddAsync(It.IsAny<WorkTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ParentIsAlreadyASubtask_ReturnsConflict()
    {
        var grandparent = new WorkTask { Id = ParentTaskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, CategoryId = CategoryId, ParentTaskId = Guid.NewGuid(), Title = "Parent", ShortId = "PRJ-1", CreatedAt = DateTimeOffset.UtcNow };
        var (handler, tasks, _) = Build(grandparent, assignee: null);

        var result = await handler.Handle(new CreateSubtaskCommand(ParentTaskId, "Title", null, null, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        tasks.Verify(x => x.AddAsync(It.IsAny<WorkTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerNotEffectiveManager_ReturnsForbidden()
    {
        var parent = new WorkTask { Id = ParentTaskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, CategoryId = CategoryId, Title = "Parent", ShortId = "PRJ-1", CreatedAt = DateTimeOffset.UtcNow };
        var (handler, tasks, _) = Build(parent, assignee: null, callerIsEffectiveManager: false);

        var result = await handler.Handle(new CreateSubtaskCommand(ParentTaskId, "Title", null, null, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        tasks.Verify(x => x.AddAsync(It.IsAny<WorkTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AssigneeNotActive_ReturnsFailure()
    {
        var parent = new WorkTask { Id = ParentTaskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, CategoryId = CategoryId, Title = "Parent", ShortId = "PRJ-1", CreatedAt = DateTimeOffset.UtcNow };
        var (handler, tasks, assignments) = Build(parent, assignee: null);

        var result = await handler.Handle(new CreateSubtaskCommand(ParentTaskId, "Title", null, null, AssigneeEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        tasks.Verify(x => x.AddAsync(It.IsAny<WorkTask>(), It.IsAny<CancellationToken>()), Times.Never);
        assignments.Verify(x => x.AddAsync(It.IsAny<TaskAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
