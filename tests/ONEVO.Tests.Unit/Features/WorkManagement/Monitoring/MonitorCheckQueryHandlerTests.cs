using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Monitoring.Queries.CheckModuleCapacity;
using ONEVO.Application.Features.WorkManagement.Monitoring.Queries.CheckTaskLoad;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Monitoring;

public class MonitorCheckQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid Caller = Guid.NewGuid();
    private static readonly DateOnly Mon5 = new(2026, 10, 5);
    private static readonly DateOnly Wed7 = new(2026, 10, 7);
    private static readonly DateOnly Fri16 = new(2026, 10, 16);

    private readonly Mock<IProjectMonitorCallerResolver> _callers = new();
    private readonly Mock<IWorkCalendarResolver> _calendars = new();
    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<IProjectMemberRepository> _members = new();
    private readonly Mock<IProjectMonitorSnapshotLoader> _snapshots = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public MonitorCheckQueryHandlerTests()
    {
        _callers.Setup(x => x.ResolveAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MonitorCaller>.Success(new MonitorCaller(TenantId, Caller, new Project { Id = ProjectId })));
        _calendars.Setup(x => x.ForProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(WorkCalendar.Default);
        _clock.SetupGet(x => x.Today).Returns(Mon5);
        _identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [Caller] = "Kaja" });
    }

    private CheckModuleCapacityQueryHandler ModuleHandler()
        => new(_callers.Object, _calendars.Object, _objectives.Object, _members.Object, _clock.Object);

    private CheckTaskLoadQueryHandler TaskHandler()
        => new(_callers.Object, _snapshots.Object, _identity.Object, _clock.Object);

    private static List<Guid> FourOthers() => [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

    [Fact]
    public async Task ModuleCheck_Create_OverCapacity_WarnsButStillReturnsTheNumbers()
    {
        // 4 members + the creator = 5 people x 10 working days x 8h = 400h.
        var result = await ModuleHandler().Handle(new CheckModuleCapacityQuery(ProjectId, null, Mon5, Fri16, 500m, FourOthers()), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(400m, result.Value!.CapacityHours);
        Assert.Equal(5, result.Value.MemberCount);
        Assert.Equal(10, result.Value.WorkingDays);
        var warning = Assert.Single(result.Value.Warnings);
        Assert.Equal(MonitorRuleCodes.ModuleOverCapacity, warning.Code);
    }

    [Fact]
    public async Task ModuleCheck_WithinCapacity_NoWarnings()
    {
        var result = await ModuleHandler().Handle(new CheckModuleCapacityQuery(ProjectId, null, Mon5, Fri16, 300m, FourOthers()), default);

        Assert.Empty(result.Value!.Warnings);
    }

    [Fact]
    public async Task ModuleCheck_Edit_CountsMembersAndOwnersOfTheModuleAndAllItsSubModules_NotTheCaller()
    {
        var owner = Guid.NewGuid();
        var member = Guid.NewGuid();
        var childOwner = Guid.NewGuid();
        var childMember = Guid.NewGuid();
        var siblingMember = Guid.NewGuid();
        var moduleId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var siblingId = Guid.NewGuid();
        var module = new Objective { Id = moduleId, ProjectId = ProjectId, OwnerId = owner, Title = "Payments" };
        _objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, moduleId, It.IsAny<CancellationToken>())).ReturnsAsync(module);
        _objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            module,
            new Objective { Id = childId, ParentObjectiveId = moduleId, ProjectId = ProjectId, OwnerId = childOwner },
            new Objective { Id = siblingId, ProjectId = ProjectId, OwnerId = Guid.NewGuid() }
        ]);
        _members.Setup(x => x.ListActiveForProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new ProjectMember { ObjectiveId = moduleId, EmployeeId = owner },
            new ProjectMember { ObjectiveId = moduleId, EmployeeId = member },
            new ProjectMember { ObjectiveId = childId, EmployeeId = childMember },
            new ProjectMember { ObjectiveId = childId, EmployeeId = member },
            new ProjectMember { ObjectiveId = siblingId, EmployeeId = siblingMember }
        ]);

        var result = await ModuleHandler().Handle(new CheckModuleCapacityQuery(ProjectId, moduleId, Mon5, Fri16, 100m, null), default);

        // owner, member, childOwner, childMember (member counted once; the sibling is not below).
        Assert.Equal(4, result.Value!.MemberCount);
        Assert.Equal(320m, result.Value.CapacityHours);
    }

    [Fact]
    public async Task ModuleCheck_CallerWithoutAccess_PassesTheFailureThrough()
    {
        _callers.Setup(x => x.ResolveAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MonitorCaller>.Forbidden("nope"));

        var result = await ModuleHandler().Handle(new CheckModuleCapacityQuery(ProjectId, null, Mon5, Fri16, 1m, null), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    private void SnapshotWith(params MonitorTask[] tasks)
        => _snapshots.Setup(x => x.LoadAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectMonitorSnapshot(ProjectId, WorkCalendar.Default, [], [], tasks));

    private static MonitorTask Existing(Guid id, decimal estimate)
        => new(id, Guid.NewGuid(), null, null, "Existing", Wed7, estimate, 0m, false, 0m, [Caller]);

    [Fact]
    public async Task TaskCheck_NewTaskOverflowsTheAssigneesWindow_WarnsWithTheirName()
    {
        SnapshotWith(Existing(Guid.NewGuid(), 20m));   // Mon 5 -> Wed 7 = 24h; 20 + 10 = 30

        var result = await TaskHandler().Handle(new CheckTaskLoadQuery(ProjectId, null, [Caller], Wed7, 10m), default);

        var warning = Assert.Single(result.Value!.Warnings);
        Assert.Equal(Caller, warning.EmployeeId);
        Assert.Equal("Kaja has 30 h of work due by Oct 7 but only 24 h of working time.", warning.Message);
    }

    [Fact]
    public async Task TaskCheck_EditingATask_ReplacesItInsteadOfCountingItTwice()
    {
        var id = Guid.NewGuid();
        SnapshotWith(Existing(id, 20m));

        var result = await TaskHandler().Handle(new CheckTaskLoadQuery(ProjectId, id, [Caller], Wed7, 22m), default);

        Assert.Empty(result.Value!.Warnings);
    }

    [Fact]
    public async Task TaskCheck_NothingToPlan_SkipsLoadingTheProject()
    {
        var result = await TaskHandler().Handle(new CheckTaskLoadQuery(ProjectId, null, [Caller], null, 10m), default);

        Assert.Empty(result.Value!.Warnings);
        _snapshots.Verify(x => x.LoadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
