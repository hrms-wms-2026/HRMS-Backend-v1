using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Monitoring.Queries.ListProjectMonitorAlerts;
using ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Monitoring;

public class ListProjectMonitorAlertsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid Caller = Guid.NewGuid();
    private static readonly Guid RootId = Guid.NewGuid();
    private static readonly Guid MineId = Guid.NewGuid();
    private static readonly Guid MyChildId = Guid.NewGuid();
    private static readonly Guid SiblingId = Guid.NewGuid();
    private static readonly Guid TaskInChild = Guid.NewGuid();
    private static readonly Guid TaskInSibling = Guid.NewGuid();

    private readonly Mock<IProjectMonitorCallerResolver> _callers = new();
    private readonly Mock<IMonitorAlertRepository> _alerts = new();
    private readonly Mock<IWorkHierarchyService> _hierarchy = new();
    private readonly Mock<IProjectMemberRepository> _members = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();

    private static MonitorAlert Alert(string type, Guid targetId) => new()
    {
        Id = Guid.NewGuid(), TargetType = type, TargetId = targetId, RuleCode = "r", Message = "m"
    };

    private readonly MonitorAlert _onMyChild = Alert(MonitorTargetTypes.Module, MyChildId);
    private readonly MonitorAlert _onSibling = Alert(MonitorTargetTypes.Module, SiblingId);
    private readonly MonitorAlert _taskInChild = Alert(MonitorTargetTypes.Task, TaskInChild);
    private readonly MonitorAlert _taskInSibling = Alert(MonitorTargetTypes.Task, TaskInSibling);
    private readonly MonitorAlert _sprint = Alert(MonitorTargetTypes.Sprint, Guid.NewGuid());

    public ListProjectMonitorAlertsQueryHandlerTests()
    {
        _alerts.Setup(x => x.ListOpenForProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_onMyChild, _onSibling, _taskInChild, _taskInSibling, _sprint]);
        _hierarchy.Setup(x => x.LoadTreeAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(new ProjectModuleTree(
        [
            new Objective { Id = RootId, OwnerId = Guid.NewGuid(), IsDefault = true },
            new Objective { Id = MineId, ParentObjectiveId = RootId, OwnerId = Guid.NewGuid() },
            new Objective { Id = MyChildId, ParentObjectiveId = MineId, OwnerId = Guid.NewGuid() },
            new Objective { Id = SiblingId, ParentObjectiveId = RootId, OwnerId = Guid.NewGuid() },
        ]));
        _members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(TenantId, ProjectId, Caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync([MineId]);
        _tasks.Setup(x => x.GetObjectiveIdsByTaskIdsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid> { [TaskInChild] = MyChildId, [TaskInSibling] = SiblingId });
    }

    private void CallerIs(Guid leadId)
        => _callers.Setup(x => x.ResolveAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MonitorCaller>.Success(new MonitorCaller(TenantId, Caller, new Project { Id = ProjectId, LeadId = leadId })));

    private ListProjectMonitorAlertsQueryHandler Handler()
        => new(_callers.Object, _alerts.Object, _hierarchy.Object, _members.Object, _tasks.Object);

    [Fact]
    public async Task Member_SeesAlertsAtOrBelowTheirModule_AndSprintAlerts()
    {
        CallerIs(leadId: Guid.NewGuid());

        var result = await Handler().Handle(new ListProjectMonitorAlertsQuery(ProjectId), default);

        Assert.Equal(
            new[] { _onMyChild.Id, _taskInChild.Id, _sprint.Id }.Order(),
            result.Value!.Select(a => a.Id).Order());
    }

    [Fact]
    public async Task ProjectLead_SeesEverything()
    {
        CallerIs(leadId: Caller);

        var result = await Handler().Handle(new ListProjectMonitorAlertsQuery(ProjectId), default);

        Assert.Equal(5, result.Value!.Count);
    }

    [Fact]
    public async Task AlertsOnOrUnderAnAchievedModule_AreFlagged()
    {
        _hierarchy.Setup(x => x.LoadTreeAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(new ProjectModuleTree(
        [
            new Objective { Id = RootId, OwnerId = Guid.NewGuid(), IsDefault = true },
            new Objective { Id = MineId, ParentObjectiveId = RootId, OwnerId = Guid.NewGuid(), IsAchieved = true },
            new Objective { Id = MyChildId, ParentObjectiveId = MineId, OwnerId = Guid.NewGuid() },
            new Objective { Id = SiblingId, ParentObjectiveId = RootId, OwnerId = Guid.NewGuid() },
        ]));
        CallerIs(leadId: Caller);

        var result = await Handler().Handle(new ListProjectMonitorAlertsQuery(ProjectId), default);

        var flagged = result.Value!.Where(a => a.InAchievedModule).Select(a => a.Id).Order();
        Assert.Equal(new[] { _onMyChild.Id, _taskInChild.Id }.Order(), flagged);
    }
}
