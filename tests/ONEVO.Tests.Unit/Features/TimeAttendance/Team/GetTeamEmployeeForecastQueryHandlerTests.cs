using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Team.Queries;
using ONEVO.Application.Features.TimeAttendance.Team.Services;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance.Team;

public class GetTeamEmployeeForecastQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly DateTimeOffset UtcNow = DateTimeOffset.Parse("2026-11-09T04:00:00+00:00"); // Mon 09:30 Colombo
    private static readonly DateOnly Today = new(2026, 11, 9);
    private static readonly DateOnly Fri13 = new(2026, 11, 13);
    private static readonly DateOnly Fri20 = new(2026, 11, 20);
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ModuleX = Guid.NewGuid();
    private static readonly Guid ModuleY = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<IEmployeeAuthorityResolver> _authority = new();
    private readonly Mock<IAttendanceReadRepository> _attendance = new();
    private readonly Mock<IConversionRateReader> _rates = new();
    private readonly Mock<IForecastTaskReader> _tasks = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces.ITaskRateSettingRepository> _rateSettings = new();
    private List<ForecastOpenTask> _open = [];

    public GetTeamEmployeeForecastQueryHandlerTests()
    {
        _user.SetupGet(x => x.IsAuthenticated).Returns(true);
        _user.SetupGet(x => x.TenantId).Returns(TenantId);
        _user.SetupGet(x => x.UserId).Returns(UserId);
        _user.Setup(x => x.HasPermission("attendance:read")).Returns(true);
        _clock.SetupGet(x => x.UtcNow).Returns(UtcNow);
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Actor, UserId = UserId, TenantId = TenantId, LegalEntityId = LegalEntityId });
        _legalEntities.Setup(x => x.GetByIdForTenantAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = LegalEntityId, TenantId = TenantId, Timezone = "Asia/Colombo", StandardWorkingDays = "[1,2,3,4,5]" });
        _authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, false, [A, B, C, Actor]));
        _attendance.Setup(x => x.ListEmployeeIdentitiesAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, AttendanceHistoryEmployee>
            {
                [A] = new(A, "Arun", "E1", null, "Eng", null),
                [B] = new(B, "Bala", "E2", null, "Eng", null),
                [C] = new(C, "Chitra", "E3", null, "Ops", null),
            });
        _rates.Setup(x => x.ReadAsync(TenantId, It.IsAny<LegalEntity?>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversionRateSet(Today, Today.AddDays(-28), Today.AddDays(-1), new Dictionary<Guid, EmployeeConversionRate>
            {
                [A] = new(A, 600, 300, 0.5m, [600, 0, 0, 0], [300, 0, 0, 0]),
                [B] = new(B, 600, 600, 1m, [600, 0, 0, 0], [600, 0, 0, 0]),
                [C] = new(C, 30, 0, null, [30, 0, 0, 0], [0, 0, 0, 0]),
            }));
        _tasks.Setup(x => x.ListOpenAssignedAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _open);
        _tasks.Setup(x => x.ListMemberModulesAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ForecastMemberModule(B, ModuleY, "Refunds", ProjectId, "Payments", Fri20)]);
    }

    private static ForecastOpenTask Task(Guid employee, Guid module, DateOnly? due, decimal? estimate, int assignees = 1, decimal completed = 0m)
        => new(Guid.NewGuid(), employee, ProjectId, "Payments", module, module == ModuleX ? "Checkout" : "Refunds", Fri20,
            due, estimate, completed, assignees);

    private GetTeamEmployeeForecastQueryHandler Handler() => new(_user.Object, _employees.Object, _legalEntities.Object, _authority.Object,
        _attendance.Object, _rates.Object, _tasks.Object, _clock.Object, _rateSettings.Object);

    [Fact]
    public async Task WithoutAttendanceRead_IsForbidden()
    {
        _user.Setup(x => x.HasPermission("attendance:read")).Returns(false);
        Assert.Equal(403, (await Handler().Handle(new GetTeamEmployeeForecastQuery(), default)).StatusCode);
    }

    [Fact]
    public async Task UnknownStatus_Is422()
        => Assert.Equal(422, (await Handler().Handle(new GetTeamEmployeeForecastQuery(Status: "late"), default)).StatusCode);

    [Fact]
    public async Task Forecasts_each_covered_employees_binding_window_at_their_rate()
    {
        // Arun: 30h due Fri 13 (5 days x 8h = 40h x 50% = 20h) + a 24h task shared with one other (12h share) due Fri 20.
        _open =
        [
            Task(A, ModuleX, Fri13, 30m),
            Task(A, ModuleX, Fri20, 24m, assignees: 2),
            Task(A, ModuleX, Today.AddDays(-1), 99m),   // overdue: not in the EDF windows
            Task(A, ModuleX, Fri20, null),              // unestimated: open but no hours
        ];

        var r = (await Handler().Handle(new GetTeamEmployeeForecastQuery(), default)).Value!;

        Assert.Equal(3, r.Total);   // the actor is stripped from the population
        var arun = r.Items[0];
        Assert.Equal("Arun", arun.Name);
        // Fri 13 window: 30h due vs 40h -> required 0.75, overtime 30 - 20 = 10 (binding: highest required rate).
        // Fri 20 window: 42h due vs 80h -> required 0.525, overtime 42 - 40 = 2. Every figure comes from Fri 13.
        Assert.Equal((Fri13, 30m, 40m, 20m, 10m), (arun.Deadline, arun.RemainingHours, arun.CapacityHours, arun.PredictedHours, arun.OvertimeHours));
        Assert.Equal(0.75m, arun.RequiredRate);
        Assert.Equal(ForecastStatuses.RaiseRate, arun.Status);
        Assert.Equal((0.5m, 600, 300), (arun.ConversionRate!.Value, arun.WorkedMinutes, arun.TaskMinutes));
        var module = Assert.Single(arun.Modules);
        Assert.Equal(("Checkout", 141m, 4), (module.Title, module.RemainingHours, module.OpenTasks));

        // Bala: no open estimated tasks -> on_track with zeros; still lists the module he is a member of.
        var bala = r.Items.Single(i => i.Name == "Bala");
        Assert.Equal((ForecastStatuses.OnTrack, (DateOnly?)null, 0m, 0m), (bala.Status, bala.Deadline, bala.RemainingHours, bala.OvertimeHours));
        Assert.Equal(("Refunds", 0), (Assert.Single(bala.Modules).Title, bala.Modules[0].OpenTasks));

        // Chitra: under an hour of attendance and nothing due -> on_track (no tasks beats no history).
        Assert.Equal(ForecastStatuses.OnTrack, r.Items.Single(i => i.Name == "Chitra").Status);

        Assert.Equal((3, 2, 1, 0, 0), (r.Totals.People, r.Totals.OnTrack, r.Totals.RaiseRate, r.Totals.Overload, r.Totals.NoHistory));
        Assert.Equal(0.9m, r.Totals.MaxTaskRate);   // no settings row: the default
        Assert.Equal((30m, 40m, 20m, 10m), (r.Totals.RemainingHours, r.Totals.CapacityHours, r.Totals.PredictedHours, r.Totals.OvertimeHours));
        Assert.Equal(0.75m, r.Totals.RequiredRate);   // 30 / 40, from the same binding windows as the rows
        Assert.Equal(Math.Round(900m / 1230m, 4), r.Totals.TeamRate);
        Assert.Equal(4, r.RateTrend.Count);
    }

    [Fact]
    public async Task TheTenantsMaxTaskRate_DecidesOverload()
    {
        // Arun needs 75% in his Fri 13 window: raise_rate at the 90% default, overload at a stored 70%.
        _open = [Task(A, ModuleX, Fri13, 30m)];
        _rateSettings.Setup(x => x.GetForLegalEntityAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.WorkManagement.Monitoring.Entities.TaskRateSetting
                { TenantId = TenantId, LegalEntityId = LegalEntityId, MaxTaskRate = 0.70m });

        var r = (await Handler().Handle(new GetTeamEmployeeForecastQuery(Status: ForecastStatuses.Overload), default)).Value!;

        Assert.Equal("Arun", Assert.Single(r.Items).Name);
        Assert.Equal(ForecastStatuses.Overload, r.Items[0].Status);
        Assert.Equal((1, 0.7m), (r.Totals.Overload, r.Totals.MaxTaskRate));
    }

    [Fact]
    public async Task NoHistory_WithWork_SaysNoHistory_AndSearchAndSortWork()
    {
        _open = [Task(C, ModuleY, Fri13, 50m)];

        var r = (await Handler().Handle(new GetTeamEmployeeForecastQuery(Sort: "rate"), default)).Value!;
        Assert.Equal(["Arun", "Bala", "Chitra"], r.Items.Select(i => i.Name));   // lowest rate first, no history last
        var chitra = r.Items[2];
        Assert.Equal(ForecastStatuses.NoHistory, chitra.Status);
        Assert.Equal((50m, 40m, 40m, 10m), (chitra.RemainingHours, chitra.CapacityHours, chitra.PredictedHours, chitra.OvertimeHours));

        var found = (await Handler().Handle(new GetTeamEmployeeForecastQuery(Search: "chi"), default)).Value!;
        Assert.Equal("Chitra", Assert.Single(found.Items).Name);
        Assert.Equal(3, found.Totals.People);
    }
}
