using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskAccessResolverTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private (TaskAccessResolver Resolver, Mock<IWorkTaskRepository> Tasks, Mock<IProjectRepository> Projects,
        Mock<IProjectMemberRepository> Members, Mock<IPermissionResolver> Permissions) Build()
    {
        var tasks = new Mock<IWorkTaskRepository>();
        var projects = new Mock<IProjectRepository>();
        var members = new Mock<IProjectMemberRepository>();
        var permissions = new Mock<IPermissionResolver>();
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeId);

        var resolver = new TaskAccessResolver(identity.Object, tasks.Object, projects.Object, members.Object, permissions.Object);
        return (resolver, tasks, projects, members, permissions);
    }

    private static WorkTask Task_(Guid objectiveId) => new() { Id = TaskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = objectiveId };
    private static Project ActiveProject() => new() { Id = ProjectId, TenantId = TenantId, IsActive = true };

    [Fact]
    public async Task ResolveViewableTaskAsync_TaskNotFound_ReturnsNotFound()
    {
        var (resolver, tasks, _, _, _) = Build();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync((WorkTask?)null);

        var result = await resolver.ResolveViewableTaskAsync(TenantId, UserId, TaskId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task ResolveViewableTaskAsync_HasProjectsReadPermission_Succeeds()
    {
        var (resolver, tasks, projects, _, permissions) = Build();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(Task_(ObjectiveId));
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveProject());
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "*" });

        var result = await resolver.ResolveViewableTaskAsync(TenantId, UserId, TaskId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmployeeId, result.Value!.CallerEmployeeId);
    }

    [Fact]
    public async Task ResolveViewableTaskAsync_NoPermissionAndNotAnObjectiveMember_ReturnsNotFound()
    {
        var (resolver, tasks, projects, members, permissions) = Build();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(Task_(ObjectiveId));
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveProject());
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        var result = await resolver.ResolveViewableTaskAsync(TenantId, UserId, TaskId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task ResolveViewableTaskAsync_NoPermissionButIsObjectiveMember_Succeeds()
    {
        var (resolver, tasks, projects, members, permissions) = Build();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(Task_(ObjectiveId));
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveProject());
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { ObjectiveId });

        var result = await resolver.ResolveViewableTaskAsync(TenantId, UserId, TaskId, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
