using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetProjectSprints;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public sealed class GetProjectSprintsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveA = Guid.NewGuid();
    private static readonly Guid ObjectiveB = Guid.NewGuid();

    private static Project ActiveProject() => new()
    {
        Id = ProjectId,
        TenantId = TenantId,
        IsActive = true,
        Name = "Project",
        Identifier = "P1",
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static Sprint Sprint(Guid objectiveId, string name) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        ObjectiveId = objectiveId,
        Name = name,
        StartDate = new DateOnly(2026, 8, 1),
        EndDate = new DateOnly(2026, 8, 31),
        Status = SprintStatuses.Active,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static GetProjectSprintsQueryHandler BuildHandler(
        Project? project,
        IReadOnlyList<Guid> accessibleObjectiveIds,
        bool hasReadPermission,
        IReadOnlyList<Sprint>? sprints = null,
        bool authenticated = true)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(authenticated);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(
                TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessibleObjectiveIds);

        var permissions = new Mock<IPermissionResolver>();
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasReadPermission ? new List<string> { "projects:read" } : new List<string>());

        var sprintRepository = new Mock<ISprintRepository>();
        sprintRepository.Setup(x => x.GetByProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sprints ?? Array.Empty<Sprint>());

        return new GetProjectSprintsQueryHandler(
            currentUser.Object, identity.Object, projects.Object, members.Object,
            permissions.Object, sprintRepository.Object);
    }

    [Fact]
    public async Task Handle_ReadPermission_ReturnsSprintsFromMultipleObjectives()
    {
        var sprints = new[] { Sprint(ObjectiveA, "A sprint"), Sprint(ObjectiveB, "B sprint") };
        var handler = BuildHandler(ActiveProject(), Array.Empty<Guid>(), hasReadPermission: true, sprints);

        var result = await handler.Handle(new GetProjectSprintsQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Contains(result.Value!, sprint => sprint.ObjectiveId == ObjectiveA);
        Assert.Contains(result.Value!, sprint => sprint.ObjectiveId == ObjectiveB);
    }

    [Fact]
    public async Task Handle_NonPrivilegedMember_ReturnsOnlyAccessibleObjectives()
    {
        var sprints = new[] { Sprint(ObjectiveA, "Visible"), Sprint(ObjectiveB, "Hidden") };
        var handler = BuildHandler(ActiveProject(), new[] { ObjectiveA }, hasReadPermission: false, sprints);

        var result = await handler.Handle(new GetProjectSprintsQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var sprint = Assert.Single(result.Value!);
        Assert.Equal(ObjectiveA, sprint.ObjectiveId);
    }

    [Fact]
    public async Task Handle_MissingProject_ReturnsNotFound()
    {
        var handler = BuildHandler(null, Array.Empty<Guid>(), hasReadPermission: true);

        var result = await handler.Handle(new GetProjectSprintsQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_UnauthenticatedCaller_ReturnsForbidden()
    {
        var handler = BuildHandler(ActiveProject(), Array.Empty<Guid>(), hasReadPermission: true, authenticated: false);

        var result = await handler.Handle(new GetProjectSprintsQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
