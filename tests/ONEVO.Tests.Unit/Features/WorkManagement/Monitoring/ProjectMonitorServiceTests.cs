using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Monitoring;

public class ProjectMonitorServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid RootId = Guid.NewGuid();
    private static readonly Guid ModuleId = Guid.NewGuid();
    private static readonly Guid RootOwner = Guid.NewGuid();
    private static readonly Guid Lead = Guid.NewGuid();
    private static readonly DateOnly Mon5 = new(2026, 10, 5);
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<IProjectMonitorSnapshotLoader> _snapshots = new();
    private readonly Mock<IMonitorAlertRepository> _alerts = new();
    private readonly Mock<IWorkHierarchyService> _hierarchy = new();
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<IWorkNotificationEngine> _notifications = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly List<MonitorAlert> _open = [];
    private readonly List<MonitorAlert> _added = [];

    public ProjectMonitorServiceTests()
    {
        _clock.SetupGet(x => x.UtcNow).Returns(Now);
        _alerts.Setup(x => x.ListOpenTrackedForProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _open);
        _alerts.Setup(x => x.AddAsync(It.IsAny<MonitorAlert>(), It.IsAny<CancellationToken>()))
            .Callback<MonitorAlert, CancellationToken>((a, _) => _added.Add(a)).Returns(Task.CompletedTask);
        _hierarchy.Setup(x => x.LoadTreeAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(new ProjectModuleTree(
        [
            new Objective { Id = RootId, OwnerId = RootOwner, IsDefault = true },
            new Objective { Id = ModuleId, ParentObjectiveId = RootId, OwnerId = Guid.NewGuid() }
        ]));
        _hierarchy.Setup(x => x.FindActiveHolderAsync(TenantId, It.IsAny<ProjectModuleTree>(), RootId, Guid.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RootOwner);
        _projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = ProjectId, LeadId = Lead });
    }

    private ProjectMonitorService Service() => new(_snapshots.Object, _alerts.Object, _hierarchy.Object, _projects.Object, _notifications.Object, _clock.Object);

    /// <summary>A sub-module (created under the root) allocated 500h for 5 people over 10 days -> over capacity.</summary>
    private void OverCapacityModule(decimal allocated = 500m)
        => _snapshots.Setup(x => x.LoadAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(new ProjectMonitorSnapshot(
            ProjectId, WorkCalendar.Default,
            [new MonitorModule(ModuleId, RootId, null, "Payments", Mon5, new DateOnly(2026, 10, 16), allocated, 0m, false, 5)], [], []));

    private static MonitorAlert OpenAlert(string ruleCode) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, TargetType = MonitorTargetTypes.Module,
        TargetId = ModuleId, RuleCode = ruleCode, Message = "old", FirstDetectedAt = Now.AddDays(-1), LastSeenAt = Now.AddDays(-1)
    };

    [Fact]
    public async Task NewProblem_OpensAnAlert_AndNotifiesTheCreatorPositionOnce()
    {
        OverCapacityModule();

        var opened = await Service().EvaluateProjectAsync(TenantId, ProjectId, Mon5);

        Assert.Equal(1, opened);
        var alert = Assert.Single(_added);
        Assert.Equal(MonitorRuleCodes.ModuleOverCapacity, alert.RuleCode);
        Assert.Equal(Now, alert.FirstDetectedAt);
        Assert.Equal(Now, alert.NotifiedAt);
        // Default creator position of a sub-module is its parent -> the root owner is notified.
        _notifications.Verify(x => x.NotifyAsync(It.Is<WorkNotificationEvent>(e =>
            e.Kind == WorkNotificationKinds.Alert && e.ActionType == "monitor.module_over_capacity"
            && e.TargetId == ModuleId && e.ActorEmployeeId == Guid.Empty
            && e.RecipientEmployeeIds.SequenceEqual(new[] { RootOwner })), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProblemStillThere_UpdatesTheOpenAlert_WithoutNotifyingAgain()
    {
        OverCapacityModule();
        var existing = OpenAlert(MonitorRuleCodes.ModuleOverCapacity);
        _open.Add(existing);

        var opened = await Service().EvaluateProjectAsync(TenantId, ProjectId, Mon5);

        Assert.Equal(0, opened);
        Assert.Empty(_added);
        Assert.Equal(Now, existing.LastSeenAt);
        Assert.NotEqual("old", existing.Message);
        Assert.Null(existing.ResolvedAt);
        _notifications.Verify(x => x.NotifyAsync(It.IsAny<WorkNotificationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProblemGone_ResolvesTheOpenAlert()
    {
        OverCapacityModule(allocated: 100m);
        var existing = OpenAlert(MonitorRuleCodes.ModuleOverCapacity);
        _open.Add(existing);

        await Service().EvaluateProjectAsync(TenantId, ProjectId, Mon5);

        Assert.Equal(Now, existing.ResolvedAt);
        Assert.Empty(_added);
    }

    [Fact]
    public async Task NoActiveHolder_FallsBackToTheProjectLead()
    {
        OverCapacityModule();
        _hierarchy.Setup(x => x.FindActiveHolderAsync(TenantId, It.IsAny<ProjectModuleTree>(), RootId, Guid.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        await Service().EvaluateProjectAsync(TenantId, ProjectId, Mon5);

        _notifications.Verify(x => x.NotifyAsync(It.Is<WorkNotificationEvent>(e =>
            e.RecipientEmployeeIds.SequenceEqual(new[] { Lead })), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TaskAlert_GoesToTheTasksCreatorPosition()
    {
        var taskId = Guid.NewGuid();
        _snapshots.Setup(x => x.LoadAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(new ProjectMonitorSnapshot(
            ProjectId, WorkCalendar.Default, [],  [],
            [new MonitorTask(taskId, ModuleId, RootId, null, "Late task", new DateOnly(2026, 10, 1), null, 0m, false, 0m, [])]));

        await Service().EvaluateProjectAsync(TenantId, ProjectId, Mon5);

        _notifications.Verify(x => x.NotifyAsync(It.Is<WorkNotificationEvent>(e =>
            e.ActionType == "monitor.task_overdue" && e.TargetType == MonitorTargetTypes.Task
            && e.RecipientEmployeeIds.SequenceEqual(new[] { RootOwner })), It.IsAny<CancellationToken>()), Times.Once);
    }
}
