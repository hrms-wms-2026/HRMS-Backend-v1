using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetProjectSprints;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
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

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<IProjectMemberRepository> _members = new();
    private readonly Mock<IPermissionResolver> _permissions = new();
    private readonly Mock<ISprintRepository> _sprints = new();
    private readonly Mock<ISprintAccessService> _access = new();

    public GetProjectSprintsQueryHandlerTests()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);

        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeId);
    }

    private static Project ActiveProject() => new()
    {
        Id = ProjectId,
        TenantId = TenantId,
        IsActive = true,
        Name = "Project",
        Identifier = "P1",
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static Sprint Sprint(string name) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        ProjectId = ProjectId,
        Name = name,
        StartDate = new DateOnly(2026, 8, 1),
        EndDate = new DateOnly(2026, 8, 31),
        Status = SprintStatuses.Active,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private GetProjectSprintsQueryHandler Build() => new(
        _currentUser.Object, _identity.Object, _projects.Object, _members.Object,
        _permissions.Object, _sprints.Object, _access.Object);

    [Fact]
    public async Task ReadPermissionCaller_SeesAllProjectSprints_WithCanManageFlags()
    {
        var mine = Sprint("A");
        var other = Sprint("B");
        _projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveProject());
        _sprints.Setup(x => x.GetByProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Sprint> { mine, other });
        _permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "*" });
        _access.Setup(x => x.GetManageableSprintIdsAsync(TenantId, ProjectId, It.IsAny<IReadOnlyList<Sprint>>(), UserId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { mine.Id });

        var result = await Build().Handle(new GetProjectSprintsQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.True(result.Value.Single(s => s.Id == mine.Id).CanManage);
        Assert.False(result.Value.Single(s => s.Id == other.Id).CanManage);
        _members.Verify(x => x.HasActiveMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Member_SeesAllProjectSprints_WithCanManageFlags()
    {
        var mine = new Sprint { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "A", Status = SprintStatuses.Draft };
        var other = new Sprint { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "B", Status = SprintStatuses.Active };
        _projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveProject());
        _sprints.Setup(x => x.GetByProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Sprint> { mine, other });
        _permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        _members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _access.Setup(x => x.GetManageableSprintIdsAsync(TenantId, ProjectId, It.IsAny<IReadOnlyList<Sprint>>(), UserId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { mine.Id });

        var result = await Build().Handle(new GetProjectSprintsQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.True(result.Value.Single(s => s.Id == mine.Id).CanManage);
        Assert.False(result.Value.Single(s => s.Id == other.Id).CanManage);
    }

    [Fact]
    public async Task NonMember_ReturnsForbidden()
    {
        _projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveProject());
        _permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        _members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await Build().Handle(new GetProjectSprintsQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_MissingProject_ReturnsNotFound()
    {
        _projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Project?)null);

        var result = await Build().Handle(new GetProjectSprintsQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_InactiveProject_ReturnsNotFound()
    {
        var inactive = ActiveProject();
        inactive.IsActive = false;
        _projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactive);

        var result = await Build().Handle(new GetProjectSprintsQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_UnauthenticatedCaller_ReturnsForbidden()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);

        var result = await Build().Handle(new GetProjectSprintsQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
