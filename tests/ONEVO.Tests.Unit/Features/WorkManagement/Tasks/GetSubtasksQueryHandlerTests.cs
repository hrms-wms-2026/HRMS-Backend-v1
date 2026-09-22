using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetSubtasks;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public sealed class GetSubtasksQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ParentTaskId = Guid.NewGuid();

    private static GetSubtasksQueryHandler Build(WorkTask? parent, IReadOnlyList<WorkTask> children, bool canRead = true)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(EmployeeId);
        identity.Setup(x => x.ResolveIdentitiesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, EmployeeIdentityDto>());

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, ParentTaskId, It.IsAny<CancellationToken>())).ReturnsAsync(parent);
        tasks.Setup(x => x.GetByParentTaskIdAsync(TenantId, ParentTaskId, It.IsAny<CancellationToken>())).ReturnsAsync(children);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new Project { Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "P", Identifier = "P", CreatedAt = DateTimeOffset.UtcNow });

        var permissions = new Mock<IPermissionResolver>();
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(canRead ? new List<string> { "projects:read" } : new List<string>());

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { ObjectiveId });

        var assignments = new Mock<ITaskAssignmentRepository>();
        assignments.Setup(x => x.GetByTaskIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TaskAssignment>());

        return new GetSubtasksQueryHandler(currentUser.Object, identity.Object, Mock.Of<IFileStorageService>(), tasks.Object,
            projects.Object, members.Object, permissions.Object, assignments.Object);
    }

    [Fact]
    public async Task Handle_ReturnsDirectChildrenWithParentId()
    {
        var parent = Task(ParentTaskId, ObjectiveId, "Parent");
        var child = Task(Guid.NewGuid(), ObjectiveId, "Child", ParentTaskId);

        var result = await Build(parent, new[] { child }).Handle(new GetSubtasksQuery(ParentTaskId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ParentTaskId, Assert.Single(result.Value!).ParentTaskId);
    }

    [Fact]
    public async Task Handle_MissingParent_ReturnsNotFound()
    {
        var result = await Build(null, Array.Empty<WorkTask>()).Handle(new GetSubtasksQuery(ParentTaskId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_InaccessibleObjective_ReturnsNotFound()
    {
        var parent = Task(ParentTaskId, Guid.NewGuid(), "Parent");

        var result = await Build(parent, Array.Empty<WorkTask>(), canRead: false)
            .Handle(new GetSubtasksQuery(ParentTaskId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    private static WorkTask Task(Guid id, Guid objectiveId, string title, Guid? parentTaskId = null) => new()
    {
        Id = id, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = objectiveId, ParentTaskId = parentTaskId,
        Title = title, ShortId = $"P-{id.ToString("N")[..6]}", CategoryId = Guid.NewGuid(), StatusId = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow
    };
}
