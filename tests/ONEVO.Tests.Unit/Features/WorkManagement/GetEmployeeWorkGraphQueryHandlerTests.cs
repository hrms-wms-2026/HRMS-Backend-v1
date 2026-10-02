using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
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
    }

    private GetEmployeeWorkGraphQueryHandler CreateHandler() =>
        new(_guard.Object, _members.Object, _objectives.Object, _projects.Object, _tasks.Object, _currentUser.Object);

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
}
