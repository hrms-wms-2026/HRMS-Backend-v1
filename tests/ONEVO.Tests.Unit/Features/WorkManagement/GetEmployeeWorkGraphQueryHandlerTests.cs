using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.EmployeeWorkGraph.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.EmployeeWorkGraph.Queries.GetEmployeeWorkGraph;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class GetEmployeeWorkGraphQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IProjectMemberRepository> _members = new();
    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IEntityAssetRepository> _entityAssets = new();
    private static readonly DateOnly Today = new(2026, 10, 2);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _projectId = Guid.NewGuid();

    public GetEmployeeWorkGraphQueryHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Saif Ahamed", "saif@test.dev",
                null, null, null, "Watercraft Engineer", null, null, "full_time", "active", null, null)));
        _members.Setup(m => m.ListActiveForEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ProjectMember>());
        _objectives.Setup(o => o.ListActiveOwnedByEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Objective>());
        _tasks.Setup(t => t.ListOpenAssignedToEmployeeAsync(_tenantId, _employeeId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenAssignedTasksPage(Array.Empty<OpenAssignedTaskRow>(), 0));
        _objectives.Setup(o => o.GetByIdsForTenantAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Objective>());
        _projects.Setup(p => p.GetActiveByIdsForTenantAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Project>());
        _clock.SetupGet(c => c.Today).Returns(Today);
        _entityAssets.Setup(e => e.GetPrimaryFileIdsByOwnerAsync(_tenantId, It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid>());
        _tasks.Setup(t => t.CountByObjectivesAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), Today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, WorkTaskCounts>());
        _tasks.Setup(t => t.CountByProjectsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), Today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, WorkTaskCounts>());
        _members.Setup(m => m.ListActiveMemberEmployeeIdsByObjectivesAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Guid>>());
        _identity.Setup(i => i.ResolveIdentitiesByEmployeeIdAsync(_tenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, EmployeeIdentityDto>());
    }

    private GetEmployeeWorkGraphQueryHandler CreateHandler() =>
        new(_guard.Object, _members.Object, _objectives.Object, _projects.Object, _tasks.Object, _currentUser.Object,
            _identity.Object, _clock.Object, _entityAssets.Object);

    private Project Proj(string name) => new() { Id = _projectId, TenantId = _tenantId, Name = name, Identifier = "P", IsActive = true };

    private Objective Obj(Guid id, string title, Guid ownerId, bool isDefault = false) =>
        new() { Id = id, TenantId = _tenantId, ProjectId = _projectId, Title = title, OwnerId = ownerId, IsDefault = isDefault, IsActive = true };

    private void ArrangeObjectivesAndProjects(params Objective[] objectives)
    {
        _objectives.Setup(o => o.GetByIdsForTenantAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(objectives);
        _projects.Setup(p => p.GetActiveByIdsForTenantAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Proj("Website") });
    }

    [Fact]
    public async Task Handle_PassesThroughGuardFailure()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Forbidden("nope"));

        var result = await CreateHandler().Handle(new GetEmployeeWorkGraphQuery(_employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        _members.Verify(m => m.ListActiveForEmployeeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EmployeeWithNoWork_ReturnsOnlyTheCenterNode()
    {
        var result = await CreateHandler().Handle(new GetEmployeeWorkGraphQuery(_employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var node = Assert.Single(result.Value!.Nodes);
        Assert.Equal($"employee:{_employeeId}", node.Id);
        Assert.Equal("employee", node.Kind);
        Assert.Equal("Saif Ahamed", node.Label);
        Assert.Equal("Watercraft Engineer", node.Sublabel);
        Assert.Empty(result.Value.Links);
        Assert.Equal(0, result.Value.HiddenTaskCount);
    }

    [Fact]
    public async Task Handle_BuildsProjectModuleAndTaskNodesWithRolesAndLinks()
    {
        var ownedModule = Guid.NewGuid();
        var memberModule = Guid.NewGuid();
        var viaTaskModule = Guid.NewGuid();
        var defaultObjective = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();

        _members.Setup(m => m.ListActiveForEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new ProjectMember { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, ObjectiveId = memberModule, EmployeeId = _employeeId, IsActive = true },
                new ProjectMember { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, ObjectiveId = defaultObjective, EmployeeId = _employeeId, IsActive = true }
            });
        _objectives.Setup(o => o.ListActiveOwnedByEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Obj(ownedModule, "Owned module", _employeeId) });
        var taskInModule = Guid.NewGuid();
        var taskInDefault = Guid.NewGuid();
        _tasks.Setup(t => t.ListOpenAssignedToEmployeeAsync(_tenantId, _employeeId, 60, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenAssignedTasksPage(new[]
            {
                new OpenAssignedTaskRow(taskInModule, "WEB-1", "Fix cart", _projectId, viaTaskModule, "active"),
                new OpenAssignedTaskRow(taskInDefault, "WEB-2", "Write docs", _projectId, defaultObjective, "not_started")
            }, TotalCount: 65));
        ArrangeObjectivesAndProjects(
            Obj(ownedModule, "Owned module", _employeeId),
            Obj(memberModule, "Member module", otherOwner),
            Obj(viaTaskModule, "Via task module", otherOwner),
            Obj(defaultObjective, "Website (default)", otherOwner, isDefault: true));

        var result = await CreateHandler().Handle(new GetEmployeeWorkGraphQuery(_employeeId), CancellationToken.None);

        var graph = result.Value!;
        var byId = graph.Nodes.ToDictionary(n => n.Id);

        Assert.Equal("project", byId[$"project:{_projectId}"].Kind);
        Assert.Equal("owner", byId[$"module:{ownedModule}"].Role);
        Assert.Equal("member", byId[$"module:{memberModule}"].Role);
        Assert.Equal("contributor", byId[$"module:{viaTaskModule}"].Role);
        Assert.DoesNotContain($"module:{defaultObjective}", byId.Keys);
        Assert.Equal("active", byId[$"task:{taskInModule}"].Status);
        Assert.Equal("WEB-1", byId[$"task:{taskInModule}"].Sublabel);
        Assert.Equal(65 - 2, graph.HiddenTaskCount);

        bool Has(string source, string target, string kind) =>
            graph.Links.Any(l => l.Source == source && l.Target == target && l.Kind == kind);

        var me = $"employee:{_employeeId}";
        var project = $"project:{_projectId}";
        Assert.True(Has(me, project, "works_on"));
        Assert.True(Has(project, $"module:{ownedModule}", "contains"));
        Assert.True(Has(me, $"module:{ownedModule}", "owns"));
        Assert.True(Has(me, $"module:{memberModule}", "member_of"));
        Assert.DoesNotContain(graph.Links, l => l.Source == me && l.Target == $"module:{viaTaskModule}");
        Assert.True(Has($"module:{viaTaskModule}", $"task:{taskInModule}", "has_task"));
        Assert.True(Has(project, $"task:{taskInDefault}", "has_task"));
    }

    [Fact]
    public async Task Handle_SkipsNodesWhoseProjectIsInactiveOrMissing()
    {
        var module = Guid.NewGuid();
        _objectives.Setup(o => o.ListActiveOwnedByEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Obj(module, "Orphan module", _employeeId) });
        _objectives.Setup(o => o.GetByIdsForTenantAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Obj(module, "Orphan module", _employeeId) });

        var result = await CreateHandler().Handle(new GetEmployeeWorkGraphQuery(_employeeId), CancellationToken.None);

        Assert.Single(result.Value!.Nodes);
        Assert.Empty(result.Value.Links);
    }

    [Fact]
    public async Task Handle_AddsModuleAndProjectStats_ModuleOwnerAndMembers_AndTaskDetails()
    {
        var module = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var memberA = Guid.NewGuid();
        var memberB = Guid.NewGuid();
        var overdueTask = Guid.NewGuid();
        var futureTask = Guid.NewGuid();
        var logoFile = Guid.NewGuid();
        _entityAssets.Setup(e => e.GetPrimaryFileIdsByOwnerAsync(_tenantId, "project", It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid> { [_projectId] = logoFile });

        var objective = Obj(module, "Hardware Integration", owner);
        objective.StartDate = new DateOnly(2026, 9, 1);
        objective.EndDate = new DateOnly(2026, 9, 30);
        objective.AllocatedHours = 120m;
        objective.CompletedHours = 48m;
        ArrangeObjectivesAndProjects(objective);
        _tasks.Setup(t => t.ListOpenAssignedToEmployeeAsync(_tenantId, _employeeId, 60, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenAssignedTasksPage(new[]
            {
                new OpenAssignedTaskRow(overdueTask, "ONX-128", "Write unit tests", _projectId, module, "active",
                    Today.AddDays(-1), "In Review", "high"),
                new OpenAssignedTaskRow(futureTask, "ONX-127", "Improve pairing flow", _projectId, module, "not_started",
                    Today, "To Do", "medium")
            }, TotalCount: 2));
        _tasks.Setup(t => t.CountByObjectivesAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), Today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, WorkTaskCounts> { [module] = new(5, 2, 2, 1, 1) });
        _tasks.Setup(t => t.CountByProjectsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), Today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, WorkTaskCounts> { [_projectId] = new(12, 4, 5, 3, 2) });
        _members.Setup(m => m.ListActiveMemberEmployeeIdsByObjectivesAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Guid>> { [module] = new[] { memberA, memberB } });
        _identity.Setup(i => i.ResolveIdentitiesByEmployeeIdAsync(_tenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, EmployeeIdentityDto>
            {
                [owner] = new("Kiru Balachandran", null),
                [memberA] = new("Alex Tan", Guid.NewGuid()),
                [memberB] = new("Priya Sharma", null)
            });

        var result = await CreateHandler().Handle(new GetEmployeeWorkGraphQuery(_employeeId), CancellationToken.None);

        var byId = result.Value!.Nodes.ToDictionary(n => n.Id);
        var moduleNode = byId[$"module:{module}"];
        Assert.Equal(new WorkGraphStats(5, 2, 2, 1, 1, 120m, 48m), moduleNode.Stats);
        Assert.Equal("Kiru Balachandran", moduleNode.Module!.Owner!.Name);
        Assert.Equal(new DateOnly(2026, 9, 1), moduleNode.Module.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 30), moduleNode.Module.EndDate);
        Assert.Equal(new[] { "Alex Tan", "Priya Sharma" }, moduleNode.Module.Members.Select(m => m.Name));
        Assert.Equal(2, moduleNode.Module.MemberCount);

        Assert.Equal(12, byId[$"project:{_projectId}"].Stats!.TotalTasks);
        Assert.Equal(logoFile, byId[$"project:{_projectId}"].LogoFileId);
        Assert.Equal(2, byId[$"project:{_projectId}"].Stats!.Overdue);

        var overdue = byId[$"task:{overdueTask}"].Task!;
        Assert.True(overdue.IsOverdue);
        Assert.Equal("In Review", overdue.StatusName);
        Assert.Equal("high", overdue.Priority);
        Assert.False(byId[$"task:{futureTask}"].Task!.IsOverdue);
    }
}
