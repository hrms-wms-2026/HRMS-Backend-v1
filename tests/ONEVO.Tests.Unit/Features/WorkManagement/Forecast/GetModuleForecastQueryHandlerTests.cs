using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Forecast.Queries.GetModuleForecast;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Forecast;

public class GetModuleForecastQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid M1 = Guid.NewGuid();
    private static readonly Guid Lead = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly DateTimeOffset UtcNow = DateTimeOffset.Parse("2026-11-09T04:00:00+00:00"); // Mon 09:30 Colombo
    private static readonly DateOnly Today = new(2026, 11, 9);
    private static readonly DateOnly Fri13 = new(2026, 11, 13);
    private static readonly DateOnly Fri20 = new(2026, 11, 20);

    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<IProjectMemberRepository> _members = new();
    private readonly Mock<IConversionRateReader> _rates = new();
    private readonly Mock<IAttendanceReadRepository> _attendance = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces.ITaskRateSettingRepository> _rateSettings = new();

    private readonly Project _project;
    private readonly Objective _root;
    private readonly Objective _parent;
    private readonly Objective _child;
    private readonly Objective _other;
    private List<ProjectMember> _myMemberships;
    private List<(Guid ObjectiveId, Guid ProjectId)> _owned;

    public GetModuleForecastQueryHandlerTests()
    {
        _project = new Project { Id = Guid.NewGuid(), TenantId = TenantId, Name = "Payments", LeadId = Lead, OwningLegalEntityId = LegalEntityId };
        _root = Module("Payments", Lead, null, 1000m, Fri20, isDefault: true);
        _parent = Module("Checkout", Actor, _root.Id, 200m, Fri20);
        _child = Module("Card form", M1, _parent.Id, 80m, Fri13);
        _other = Module("Refunds", Lead, _root.Id, 50m, Fri20);
        _myMemberships = [new ProjectMember { ProjectId = _project.Id, ObjectiveId = _parent.Id, EmployeeId = Actor }];
        _owned = [(_parent.Id, _project.Id)];

        _user.SetupGet(x => x.IsAuthenticated).Returns(true);
        _user.SetupGet(x => x.TenantId).Returns(TenantId);
        _user.SetupGet(x => x.UserId).Returns(UserId);
        _clock.SetupGet(x => x.UtcNow).Returns(UtcNow);
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Actor, UserId = UserId, TenantId = TenantId, LegalEntityId = LegalEntityId });
        _legalEntities.Setup(x => x.GetByIdForTenantAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = LegalEntityId, TenantId = TenantId, Timezone = "Asia/Colombo", StandardWorkingDays = "[1,2,3,4,5]" });
        _members.Setup(x => x.ListActiveForEmployeeAsync(TenantId, Actor, It.IsAny<CancellationToken>())).ReturnsAsync(() => _myMemberships);
        _objectives.Setup(x => x.ListActiveOwnedIdsAsync(TenantId, Actor, LegalEntityId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _owned);
        _projects.Setup(x => x.GetActiveByIdsForTenantAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([_project]);
        _objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, _project.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_root, _parent, _child, _other]);
        _members.Setup(x => x.ListActiveForProjectAsync(TenantId, _project.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ProjectMember { ProjectId = _project.Id, ObjectiveId = _parent.Id, EmployeeId = Actor },
                new ProjectMember { ProjectId = _project.Id, ObjectiveId = _child.Id, EmployeeId = M1 },
            ]);
        // Both at 50% over the last 4 weeks.
        _rates.Setup(x => x.ReadAsync(TenantId, It.IsAny<LegalEntity?>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversionRateSet(Today, Today.AddDays(-28), Today.AddDays(-1), new Dictionary<Guid, EmployeeConversionRate>
            {
                [Actor] = new(Actor, 600, 300, 0.5m, [600, 0, 0, 0], [300, 0, 0, 0]),
                [M1] = new(M1, 600, 300, 0.5m, [0, 0, 0, 600], [0, 0, 0, 300]),
            }));
        _attendance.Setup(x => x.ListEmployeeIdentitiesAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, AttendanceHistoryEmployee>
            {
                [Actor] = new(Actor, "Kaja", "E1", null, "Eng", null),
                [M1] = new(M1, "Nila", "E2", null, "Eng", null),
            });
    }

    private Objective Module(string title, Guid owner, Guid? parent, decimal allocated, DateOnly end, bool isDefault = false) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = _project?.Id ?? Guid.Empty, Title = title, OwnerId = owner,
        ParentObjectiveId = parent, AllocatedHours = allocated, StartDate = Today.AddDays(-14), EndDate = end, IsDefault = isDefault,
    };

    private GetModuleForecastQueryHandler Handler() => new(_user.Object, _employees.Object, _legalEntities.Object, _projects.Object,
        _objectives.Object, _members.Object, _rates.Object, _attendance.Object, _clock.Object, _rateSettings.Object);

    [Fact]
    public async Task Unauthenticated_IsForbidden()
    {
        _user.SetupGet(x => x.IsAuthenticated).Returns(false);
        var result = await Handler().Handle(new GetModuleForecastQuery(), default);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task UnknownSort_Is422()
        => Assert.Equal(422, (await Handler().Handle(new GetModuleForecastQuery(Sort: "bogus"), default)).StatusCode);

    [Fact]
    public async Task Owner_SeesTheirModuleAndItsChildren_WithRateAwareFigures()
    {
        var r = (await Handler().Handle(new GetModuleForecastQuery(), default)).Value!;

        Assert.Equal((Today, Today.AddDays(-28), Today.AddDays(-1)), (r.AsOf, r.WindowStart, r.WindowEnd));
        Assert.Equal(2, r.Total);
        Assert.Equal(["Checkout", "Card form"], r.Items.Select(i => i.Title));

        // Checkout: Actor + M1 (child manpower) = 2 people x 10 days x 8h = 160h, x 50% = 80h vs 200h.
        var parent = r.Items[0];
        Assert.Equal((200m, 160m, 80m, 0.5m, 1.25m, 120m), (parent.RemainingHours, parent.CapacityHours, parent.PredictedHours,
            parent.ConversionRate!.Value, parent.RequiredRate!.Value, parent.OvertimeHours));
        Assert.Equal(ForecastStatuses.Overload, parent.Status);
        Assert.Equal(10, parent.WorkingDaysLeft);
        Assert.Equal(2, parent.MemberCount);
        Assert.Equal("Checkout", parent.Path);

        var child = r.Items[1];
        Assert.Equal("Checkout › Card form", child.Path);
        Assert.Equal((80m, 40m, 20m, 60m), (child.RemainingHours, child.CapacityHours, child.PredictedHours, child.OvertimeHours));

        // Totals count the top-most module only (Checkout already holds Card form's allocation); counts every listed module.
        // Both need more than the 90% default limit (125% and 200%): overload.
        Assert.Equal((2, 0, 0, 2, 0), (r.Totals.Modules, r.Totals.OnTrack, r.Totals.RaiseRate, r.Totals.Overload, r.Totals.NoHistory));
        Assert.Equal(0.9m, r.Totals.MaxTaskRate);
        Assert.Equal((200m, 200m, 160m, 80m, 120m), (r.Totals.AllocatedHours, r.Totals.RemainingHours, r.Totals.CapacityHours,
            r.Totals.PredictedHours, r.Totals.OvertimeHours));
        Assert.Equal(0.5m, r.Totals.TeamRate);
        Assert.Equal(1.25m, r.Totals.RequiredRate);

        var bar = Assert.Single(r.Projects);
        Assert.Equal(("Payments", 200m, 2), (bar.Name, bar.AllocatedHours, bar.ModulesAtRisk));

        Assert.Equal(4, r.RateTrend.Count);
        Assert.Equal(0.5m, r.RateTrend[0].Rate);
        Assert.Null(r.RateTrend[1].Rate);
    }

    [Fact]
    public async Task Owner_SeesEachMembersRate_PlainMemberSeesNothing()
    {
        var owner = (await Handler().Handle(new GetModuleForecastQuery(), default)).Value!;
        var nila = owner.Items[0].Members.Single(m => m.EmployeeId == M1);
        Assert.Equal(("Nila", 0.5m, 600, 300), (nila.Name, nila.ConversionRate!.Value, nila.WorkedMinutes, nila.TaskMinutes));

        // A plain member of Checkout (not its owner, not the project lead) leads nothing here.
        _parent.OwnerId = Guid.NewGuid();
        _owned = [];
        var member = (await Handler().Handle(new GetModuleForecastQuery(), default)).Value!;
        Assert.Empty(member.Items);
        Assert.Equal(0, member.Totals.Modules);
    }

    [Fact]
    public async Task ProjectLead_SeesEveryOpenModule_AndTheRootAloneDrivesTheTotals()
    {
        _projects.Setup(x => x.GetActiveByIdsForTenantAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Project { Id = _project.Id, TenantId = TenantId, Name = "Payments", LeadId = Actor, OwningLegalEntityId = LegalEntityId }]);

        var r = (await Handler().Handle(new GetModuleForecastQuery(Sort: "name"), default)).Value!;

        Assert.Equal(["Card form", "Checkout", "Payments", "Refunds"], r.Items.Select(i => i.Title));
        Assert.Equal(1000m, r.Totals.AllocatedHours);
    }

    [Fact]
    public async Task StatusFilter_And_Paging_KeepTheTotals()
    {
        var none = (await Handler().Handle(new GetModuleForecastQuery(Status: ForecastStatuses.RaiseRate), default)).Value!;
        Assert.Empty(none.Items);
        Assert.Equal(0, none.Total);
        Assert.Equal(2, none.Totals.Modules);

        var otherProject = (await Handler().Handle(new GetModuleForecastQuery(ProjectId: Guid.NewGuid()), default)).Value!;
        Assert.Empty(otherProject.Items);
        Assert.Equal((2, 200m), (otherProject.Totals.Modules, otherProject.Totals.AllocatedHours));
        Assert.Single(otherProject.Projects);

        var all = (await Handler().Handle(new GetModuleForecastQuery(PageSize: 500), default)).Value!;
        Assert.Equal(500, all.PageSize);

        var paged = (await Handler().Handle(new GetModuleForecastQuery(Page: 2, PageSize: 1), default)).Value!;
        Assert.Equal("Card form", Assert.Single(paged.Items).Title);
        Assert.Equal((2, 2, 1), (paged.Total, paged.Page, paged.PageSize));
    }

    [Fact]
    public async Task AchievedOrEndedModules_AndOtherLegalEntities_AreLeftOut()
    {
        _child.IsAchieved = true;
        var r = (await Handler().Handle(new GetModuleForecastQuery(), default)).Value!;
        Assert.Equal(["Checkout"], r.Items.Select(i => i.Title));

        _project.OwningLegalEntityId = Guid.NewGuid();
        Assert.Empty((await Handler().Handle(new GetModuleForecastQuery(), default)).Value!.Items);
    }
}
