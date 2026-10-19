using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.EmployeeWorkGraph.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectWorkGraph.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.ProjectWorkGraph.Queries.GetProjectWorkGraph;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class GetProjectWorkGraphQueryHandlerTests
{
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<IProjectMemberRepository> _members = new();
    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private static readonly DateOnly Today = new(2026, 10, 19);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _callerEmployeeId = Guid.NewGuid();
    private readonly Guid _projectId = Guid.NewGuid();
    private readonly Guid _rootId = Guid.NewGuid();
    private readonly Guid _childId = Guid.NewGuid();

    public GetProjectWorkGraphQueryHandlerTests()
    {
        _currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);
        _identity.Setup(i => i.ResolveCallerEmployeeIdAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_callerEmployeeId);
        _identity.Setup(i => i.ResolveIdentitiesByEmployeeIdAsync(_tenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, EmployeeIdentityDto>());
        _projects.Setup(p => p.GetByIdForTenantAsync(_tenantId, _projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Proj());
        _members.Setup(m => m.HasActiveMembershipAsync(_tenantId, _projectId, _callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _members.Setup(m => m.ListActiveMemberEmployeeIdsByObjectivesAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Guid>>());
        _objectives.Setup(o => o.GetTreeByProjectIdAsync(_tenantId, _projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Objective>());
        _tasks.Setup(t => t.CountByObjectivesAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), Today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, WorkTaskCounts>());
        _clock.SetupGet(c => c.Today).Returns(Today);
    }

    private GetProjectWorkGraphQueryHandler CreateHandler() =>
        new(_currentUser.Object, _identity.Object, _projects.Object, _members.Object,
            _objectives.Object, _tasks.Object, _clock.Object);

    private Project Proj() =>
        new() { Id = _projectId, TenantId = _tenantId, Name = "Hardware integration portal", Identifier = "HIP", IsActive = true };

    private Objective Obj(Guid id, string title, Guid? parentId, bool isDefault = false,
        decimal allocated = 0m, decimal completed = 0m) =>
        new()
        {
            Id = id,
            TenantId = _tenantId,
            ProjectId = _projectId,
            ParentObjectiveId = parentId,
            Title = title,
            OwnerId = _callerEmployeeId,
            IsDefault = isDefault,
            IsActive = true,
            StartDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2026, 12, 15),
            AllocatedHours = allocated,
            CompletedHours = completed
        };

    private void ArrangeTree(params Objective[] objectives) =>
        _objectives.Setup(o => o.GetTreeByProjectIdAsync(_tenantId, _projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(objectives);

    private Task<Result<ProjectWorkGraphResponse>> Run() =>
        CreateHandler().Handle(new GetProjectWorkGraphQuery(_projectId), CancellationToken.None);

    [Fact]
    public async Task Handle_ReturnsRootModuleAndDescendants_AsModuleNodes()
    {
        ArrangeTree(Obj(_rootId, "Hardware integration portal", null, isDefault: true),
            Obj(_childId, "Device Connectivity Framework", _rootId));

        var result = await Run();

        Assert.True(result.IsSuccess);
        var modules = result.Value!.Nodes.Where(n => n.Kind == WorkGraphNodeKinds.Module).ToList();
        Assert.Equal(2, modules.Count);
        Assert.Contains(modules, n => n.Id == $"module:{_rootId}" && n.Label == "Hardware integration portal");
        Assert.Contains(modules, n => n.Id == $"module:{_childId}" && n.Label == "Device Connectivity Framework");
        Assert.All(modules, n => Assert.Null(n.Role));
    }

    [Fact]
    public async Task Handle_LinksEachChildToItsParent_WithContainsKind()
    {
        ArrangeTree(Obj(_rootId, "Root", null, isDefault: true), Obj(_childId, "Child", _rootId));

        var result = await Run();

        var link = Assert.Single(result.Value!.Links);
        Assert.Equal($"module:{_rootId}", link.Source);
        Assert.Equal($"module:{_childId}", link.Target);
        Assert.Equal(WorkGraphLinkKinds.Contains, link.Kind);
    }

    [Fact]
    public async Task Handle_SetsStatsFromTaskCounts_IncludingAllocatedAndCompletedHours()
    {
        ArrangeTree(Obj(_rootId, "Root", null, isDefault: true, allocated: 2600m, completed: 390m));
        _tasks.Setup(t => t.CountByObjectivesAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), Today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, WorkTaskCounts> { [_rootId] = new(5, 2, 1, 2, 1) });

        var result = await Run();

        var stats = Assert.Single(result.Value!.Nodes).Stats;
        Assert.NotNull(stats);
        Assert.Equal(5, stats!.TotalTasks);
        Assert.Equal(2600m, stats.AllocatedHours);
        Assert.Equal(390m, stats.CompletedHours);
    }

    [Fact]
    public async Task Handle_ReturnsForbidden_WhenCallerIsNotAnActiveProjectMember()
    {
        _members.Setup(m => m.HasActiveMembershipAsync(_tenantId, _projectId, _callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Run();

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        _objectives.Verify(o => o.GetTreeByProjectIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _tasks.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenProjectIsMissingOrInactive()
    {
        _projects.Setup(p => p.GetByIdForTenantAsync(_tenantId, _projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Project?)null);

        var result = await Run();

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsSingleNodeAndNoLinks_WhenRootHasNoChildren()
    {
        ArrangeTree(Obj(_rootId, "Root", null, isDefault: true));

        var result = await Run();

        Assert.Single(result.Value!.Nodes);
        Assert.Empty(result.Value!.Links);
        Assert.Equal(0, result.Value!.HiddenTaskCount);
    }
}
