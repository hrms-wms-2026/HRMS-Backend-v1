using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.DuplicateTask;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class DuplicateTaskCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid SourceObjectiveId = Guid.NewGuid();
    private static readonly Guid OtherObjectiveId = Guid.NewGuid();
    private static readonly Guid SourceTaskId = Guid.NewGuid();
    private static readonly Guid DefaultStatusId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();

    private static WorkTask SourceTask() => new()
    {
        Id = SourceTaskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = SourceObjectiveId,
        ShortId = "WEB-1", Title = "Original", Description = "<p>hi</p>", CategoryId = CategoryId,
        StatusId = Guid.NewGuid(), Priority = WorkTaskPriorities.High, StoryPoints = 5,
        DueDate = new DateOnly(2026, 5, 1), EstimatedHours = 10m, SprintId = SprintId,
        CompletedHours = 8m, ProgressPercent = 80, StartedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    };

    private static Objective Owned(Guid id, decimal allocatedHours = 100m) => new()
    {
        Id = id, TenantId = TenantId, ProjectId = ProjectId, OwnerId = EmployeeId,
        IsActive = true, AllocatedHours = allocatedHours, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow
    };

    private (DuplicateTaskCommandHandler Handler, Mock<IWorkTaskRepository> Tasks,
        Mock<ITaskAssignmentRepository> Assignments, Mock<ITaskCommentRepository> Comments,
        Mock<ITaskAssetLinker> AssetLinker, Mock<ISprintRepository> Sprints) BuildHandler(
        WorkTask source, Objective destinationObjective,
        Guid? callerEmployeeId = null, bool? callerIsEffectiveManager = null,
        decimal existingAllocationSum = 0m, string sprintStatus = SprintStatuses.Active,
        Mock<ICalendarEventRepository>? calendarEvents = null,
        IReadOnlyList<TaskAssignment>? sourceAssignments = null,
        IReadOnlyList<TaskComment>? sourceComments = null,
        Employee? resolvedAssignee = null)
    {
        var resolvedCallerEmployeeId = callerEmployeeId ?? EmployeeId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedCallerEmployeeId);

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, source.Id, It.IsAny<CancellationToken>())).ReturnsAsync(source);
        tasks.Setup(x => x.GetActiveAllocationSumByObjectiveIdAsync(TenantId, destinationObjective.Id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAllocationSum);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, destinationObjective.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(destinationObjective);
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, destinationObjective.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective>());

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = ProjectId, TenantId = TenantId, Identifier = "WEB", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        projects.Setup(x => x.IncrementAndGetNextTaskNumberAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(9L);

        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskStatusEntity>
            {
                new() { Id = DefaultStatusId, TenantId = TenantId, ProjectId = ProjectId, Name = "To Do", DisplayOrder = 0, Category = TaskStatusCategories.NotStarted, CreatedAt = DateTimeOffset.UtcNow }
            });

        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sprint
            {
                Id = SprintId, TenantId = TenantId, ProjectId = ProjectId,
                Name = "Sprint 1", Status = sprintStatus, CreatedAt = DateTimeOffset.UtcNow
            });

        var slackCalculator = new ObjectiveAllocationSlackCalculator(objectives.Object, tasks.Object);

        var assignments = new Mock<ITaskAssignmentRepository>();
        assignments.Setup(x => x.GetByTaskIdAsync(source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceAssignments ?? new List<TaskAssignment>());

        var comments = new Mock<ITaskCommentRepository>();
        comments.Setup(x => x.GetForTaskAsync(TenantId, source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceComments ?? new List<TaskComment>());

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<WorkTaskResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<WorkTaskResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, destinationObjective.Id, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (destinationObjective.OwnerId == resolvedCallerEmployeeId));
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tid, Guid empId, CancellationToken _) => resolvedAssignee is not null && resolvedAssignee.Id == empId ? resolvedAssignee : null);

        var assetLinker = new Mock<ITaskAssetLinker>();

        var handler = new DuplicateTaskCommandHandler(
            currentUser.Object, identity.Object, tasks.Object, objectives.Object, projects.Object,
            statuses.Object, sprints.Object, assignments.Object, comments.Object, slackCalculator,
            unitOfWork.Object, membership.Object, (calendarEvents ?? CalendarEventRepositoryMocks.Empty()).Object,
            assetLinker.Object);

        return (handler, tasks, assignments, comments, assetLinker, sprints);
    }

    private static DuplicateTaskCommand Command(
        Guid destinationObjectiveId, string title = "Original (copy)",
        bool copyAttachments = false, bool copyAssignees = false, bool copyComments = false, bool copyDueDate = true) =>
        new(SourceTaskId, destinationObjectiveId, title, copyAttachments, copyAssignees, copyComments, copyDueDate);

    [Fact]
    public async Task Handle_SameModule_CopiesCoreFieldsResetsProgressAndUsesDefaultStatus()
    {
        var (handler, tasks, _, _, _, _) = BuildHandler(SourceTask(), Owned(SourceObjectiveId));

        var result = await handler.Handle(Command(SourceObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Original (copy)", result.Value!.Title);
        Assert.Equal(DefaultStatusId, result.Value.StatusId);
        Assert.Equal(0, result.Value.ProgressPercent);
        Assert.Equal(0m, result.Value.CompletedHours);
        tasks.Verify(x => x.AddAsync(It.Is<WorkTask>(t =>
            t.Id != SourceTaskId && t.Description == "<p>hi</p>" && t.CategoryId == CategoryId &&
            t.Priority == WorkTaskPriorities.High && t.StoryPoints == 5 && t.EstimatedHours == 10m &&
            t.ParentTaskId == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CallerNotManagerOfDestination_ReturnsForbiddenAndCreatesNothing()
    {
        var (handler, tasks, _, _, _, _) = BuildHandler(
            SourceTask(), Owned(SourceObjectiveId), callerEmployeeId: Guid.NewGuid(), callerIsEffectiveManager: false);

        var result = await handler.Handle(Command(SourceObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        tasks.Verify(x => x.AddAsync(It.IsAny<WorkTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SourceTaskNotFound_ReturnsNotFound()
    {
        var (handler, _, _, _, _, _) = BuildHandler(SourceTask(), Owned(SourceObjectiveId));
        var missingTaskCommand = new DuplicateTaskCommand(Guid.NewGuid(), SourceObjectiveId, "Copy", false, false, false, true);

        var result = await handler.Handle(missingTaskCommand, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DifferentModuleInSameProject_DropsSprintId()
    {
        var destination = Owned(OtherObjectiveId);
        var (handler, tasks, _, _, _, sprints) = BuildHandler(SourceTask(), destination);

        var result = await handler.Handle(Command(OtherObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.SprintId);
        tasks.Verify(x => x.AddAsync(It.Is<WorkTask>(t => t.SprintId == null), It.IsAny<CancellationToken>()), Times.Once);
        sprints.Verify(x => x.GetByIdForTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SameModule_KeepsValidSprintId()
    {
        var (handler, tasks, _, _, _, _) = BuildHandler(SourceTask(), Owned(SourceObjectiveId));

        var result = await handler.Handle(Command(SourceObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintId, result.Value!.SprintId);
    }

    [Fact]
    public async Task Handle_SameModuleAchievedSprint_ReturnsConflict()
    {
        var (handler, tasks, _, _, _, _) = BuildHandler(SourceTask(), Owned(SourceObjectiveId), sprintStatus: SprintStatuses.Achieved);

        var result = await handler.Handle(Command(SourceObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        tasks.Verify(x => x.AddAsync(It.IsAny<WorkTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CopyDueDateFalse_TaskHasNoDueDate()
    {
        var (handler, _, _, _, _, _) = BuildHandler(SourceTask(), Owned(SourceObjectiveId));

        var result = await handler.Handle(Command(SourceObjectiveId, copyDueDate: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.DueDate);
    }

    [Fact]
    public async Task Handle_EstimatedHoursExceedsDestinationSlack_ReturnsConflictAndCreatesNothing()
    {
        var (handler, tasks, _, _, _, _) = BuildHandler(
            SourceTask(), Owned(SourceObjectiveId, allocatedHours: 15m), existingAllocationSum: 10m);

        var result = await handler.Handle(Command(SourceObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("\"availableSlackHours\"", result.Error);
        tasks.Verify(x => x.AddAsync(It.IsAny<WorkTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DestinationInActiveEventAndCopyDueDateFalse_ReturnsConflict()
    {
        var calendarEvents = CalendarEventRepositoryMocks.Empty();
        calendarEvents.Setup(x => x.ListActiveEventWindowsForObjectiveAsync(TenantId, SourceObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ActiveEventWindow(Guid.NewGuid(), "Release", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)) });
        var (handler, tasks, _, _, _, _) = BuildHandler(SourceTask(), Owned(SourceObjectiveId), calendarEvents: calendarEvents);

        var result = await handler.Handle(Command(SourceObjectiveId, copyDueDate: false), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        tasks.Verify(x => x.AddAsync(It.IsAny<WorkTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CopyAttachmentsTrue_DelegatesToAssetLinkerWithSourceAndNewTaskIds()
    {
        var (handler, _, _, _, assetLinker, _) = BuildHandler(SourceTask(), Owned(SourceObjectiveId));

        var result = await handler.Handle(Command(SourceObjectiveId, copyAttachments: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        assetLinker.Verify(x => x.CopyAttachmentsAsync(TenantId, UserId, SourceTaskId, result.Value!.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CopyAttachmentsFalse_NeverCallsAssetLinkerCopy()
    {
        var (handler, _, _, _, assetLinker, _) = BuildHandler(SourceTask(), Owned(SourceObjectiveId));

        var result = await handler.Handle(Command(SourceObjectiveId, copyAttachments: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        assetLinker.Verify(x => x.CopyAttachmentsAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CopyAssigneesTrue_ActiveAssigneeCopiedWithFreshResolvedUserId()
    {
        var assigneeEmployeeId = Guid.NewGuid();
        var assigneeUserId = Guid.NewGuid();
        var activeAssignee = new Employee { Id = assigneeEmployeeId, TenantId = TenantId, UserId = assigneeUserId, EmployeeNumber = "E1", HireDate = new DateOnly(2020, 1, 1) };
        var sourceAssignments = new List<TaskAssignment>
        {
            new() { Id = Guid.NewGuid(), TaskId = SourceTaskId, EmployeeId = assigneeEmployeeId, UserId = Guid.NewGuid() /* stale on purpose */, AssignedById = EmployeeId, AssignedAt = DateTimeOffset.UtcNow }
        };
        var (handler, _, assignments, _, _, _) = BuildHandler(
            SourceTask(), Owned(SourceObjectiveId), sourceAssignments: sourceAssignments, resolvedAssignee: activeAssignee);

        var result = await handler.Handle(Command(SourceObjectiveId, copyAssignees: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        assignments.Verify(x => x.AddAsync(It.Is<TaskAssignment>(a =>
            a.TaskId == result.Value!.Id && a.EmployeeId == assigneeEmployeeId && a.UserId == assigneeUserId &&
            a.AssignedById == EmployeeId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CopyAssigneesTrue_InactiveAssigneeIsSkipped()
    {
        var inactiveEmployeeId = Guid.NewGuid();
        var sourceAssignments = new List<TaskAssignment>
        {
            new() { Id = Guid.NewGuid(), TaskId = SourceTaskId, EmployeeId = inactiveEmployeeId, UserId = Guid.NewGuid(), AssignedById = EmployeeId, AssignedAt = DateTimeOffset.UtcNow }
        };
        var (handler, _, assignments, _, _, _) = BuildHandler(
            SourceTask(), Owned(SourceObjectiveId), sourceAssignments: sourceAssignments, resolvedAssignee: null);

        var result = await handler.Handle(Command(SourceObjectiveId, copyAssignees: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        assignments.Verify(x => x.AddAsync(It.IsAny<TaskAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CopyCommentsTrue_CopiesTopLevelAndReplyPreservingHierarchy_SkipsDeleted()
    {
        var topLevelId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
        var sourceComments = new List<TaskComment>
        {
            new() { Id = topLevelId, TenantId = TenantId, TaskId = SourceTaskId, EmployeeId = EmployeeId, Content = "Root", CreatedAt = DateTimeOffset.UtcNow },
            new() { Id = deletedId, TenantId = TenantId, TaskId = SourceTaskId, EmployeeId = EmployeeId, Content = "Gone", IsDeleted = true, CreatedAt = DateTimeOffset.UtcNow },
            new() { Id = Guid.NewGuid(), TenantId = TenantId, TaskId = SourceTaskId, EmployeeId = EmployeeId, ParentCommentId = topLevelId, Content = "Reply", CreatedAt = DateTimeOffset.UtcNow }
        };
        var (handler, _, _, comments, _, _) = BuildHandler(
            SourceTask(), Owned(SourceObjectiveId), sourceComments: sourceComments);

        var result = await handler.Handle(Command(SourceObjectiveId, copyComments: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        comments.Verify(x => x.AddAsync(It.Is<TaskComment>(c => c.Content == "Root" && c.ParentCommentId == null && c.TaskId == result.Value!.Id), It.IsAny<CancellationToken>()), Times.Once);
        comments.Verify(x => x.AddAsync(It.Is<TaskComment>(c => c.Content == "Reply" && c.ParentCommentId != null && c.ParentCommentId != topLevelId), It.IsAny<CancellationToken>()), Times.Once);
        comments.Verify(x => x.AddAsync(It.Is<TaskComment>(c => c.Content == "Gone"), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DestinationObjectiveInDifferentProject_ReturnsConflictAndCreatesNothing()
    {
        var foreignProjectObjective = new Objective
        {
            Id = OtherObjectiveId, TenantId = TenantId, ProjectId = Guid.NewGuid(), OwnerId = EmployeeId,
            IsActive = true, AllocatedHours = 100m, Title = "Foreign", CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, tasks, _, _, _, _) = BuildHandler(SourceTask(), foreignProjectObjective);

        var result = await handler.Handle(Command(OtherObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        tasks.Verify(x => x.AddAsync(It.IsAny<WorkTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
