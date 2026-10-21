using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedEmployeeHours;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedProgressTasks;
using ONEVO.Application.Features.WorkManagement.Leadership.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Leadership;

/// <summary>GET led-progress/employees and led-progress/tasks handlers: gate, scope narrowing,
/// paging clamps and what reaches the repository.</summary>
public sealed class LedProgressDetailQueryHandlerTests
{
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IWorkLeadershipService> _leadership = new();
    private readonly Mock<ILedProgressReadRepository> _reads = new();
    // Wednesday 2026-10-21, late in the UTC day: ISO week 19..25 Oct, month 1..31 Oct.
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = new DateTimeOffset(2026, 10, 21, 23, 30, 0, TimeSpan.Zero) };
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Employee _caller;

    // Scope: project P1 head A (children B, and C under B); project P2 head D.
    private readonly Guid _p1 = Guid.NewGuid(), _p2 = Guid.NewGuid();
    private readonly Guid _a = Guid.NewGuid(), _b = Guid.NewGuid(), _c = Guid.NewGuid(), _d = Guid.NewGuid();

    private LedEmployeeHoursFilter? _employeeFilter;
    private LedProgressTaskFilter? _taskFilter;
    private IReadOnlyCollection<Guid>? _unassignedIds;
    private LedDueWindow? _unassignedWindow;

    public LedProgressDetailQueryHandlerTests()
    {
        _caller = new Employee { Id = Guid.NewGuid(), TenantId = _tenantId, UserId = _userId, LegalEntityId = _legalEntityId };
        _user.SetupGet(u => u.IsAuthenticated).Returns(true);
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(_userId);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(_caller);
        _leadership.Setup(l => l.ResolveLedScopeAsync(_tenantId, _caller.Id, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Scope());

        _reads.Setup(r => r.ListEmployeeHoursAsync(_tenantId, It.IsAny<LedEmployeeHoursFilter>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, LedEmployeeHoursFilter, CancellationToken>((_, f, _) => _employeeFilter = f)
            .ReturnsAsync(new LedPagedRows<LedEmployeeHoursRow>(
                [new LedEmployeeHoursRow(Guid.NewGuid(), "Ada Lovelace", 5, 3, 2, 10, 3, 1, 3)], 41));
        _reads.Setup(r => r.GetUnassignedHoursAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<LedDueWindow?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyCollection<Guid>, LedDueWindow?, CancellationToken>((_, ids, window, _) =>
            {
                _unassignedIds = ids;
                _unassignedWindow = window;
            })
            .ReturnsAsync(new LedUnassignedHoursRow(1, 2, 3, 6, 4));
        _reads.Setup(r => r.ListTasksAsync(_tenantId, It.IsAny<LedProgressTaskFilter>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, LedProgressTaskFilter, CancellationToken>((_, f, _) => _taskFilter = f)
            .ReturnsAsync(new LedPagedRows<LedProgressTaskRow>(
            [
                TaskRow(LedTaskBucket.InProgress, new LedProgressTaskAssigneeRow(Guid.NewGuid(), "Ada"), new LedProgressTaskAssigneeRow(Guid.NewGuid(), "Bob")),
                TaskRow(LedTaskBucket.Completed),
            ], 2));
    }

    private LedWorkScope Scope()
    {
        var end = new DateOnly(2026, 12, 31);
        var rowsA = new List<LedObjectiveRow>
        {
            new(_a, _p1, null, "A", false, end), new(_b, _p1, _a, "B", false, end), new(_c, _p1, _b, "C", false, end),
        };
        var rowsD = new List<LedObjectiveRow> { new(_d, _p2, null, "D", false, end) };
        return new LedWorkScope(
        [
            new LedHeadModule(_a, _p1, "A", false, end, new HashSet<Guid> { _a, _b, _c }, 0, rowsA),
            new LedHeadModule(_d, _p2, "D", false, end, new HashSet<Guid> { _d }, 0, rowsD),
        ], []);
    }

    private LedProgressTaskRow TaskRow(LedTaskBucket bucket, params LedProgressTaskAssigneeRow[] assignees) => new(
        Guid.NewGuid(), "T-1", "Task", bucket, "Doing", 4, new DateOnly(2026, 11, 1), _p1, "Project", _a, "A",
        assignees, assignees.Length);

    private GetLedEmployeeHoursQueryHandler EmployeeHandler() => new(_user.Object, _clock, _employees.Object, _leadership.Object, _reads.Object);
    private GetLedProgressTasksQueryHandler TaskHandler() => new(_user.Object, _clock, _employees.Object, _leadership.Object, _reads.Object);

    // ---------- gate ----------

    [Fact]
    public async Task Unauthenticated_caller_is_forbidden_on_both_endpoints()
    {
        _user.SetupGet(u => u.IsAuthenticated).Returns(false);

        (await EmployeeHandler().Handle(new GetLedEmployeeHoursQuery(), default)).StatusCode.Should().Be(403);
        (await TaskHandler().Handle(new GetLedProgressTasksQuery(), default)).StatusCode.Should().Be(403);
        _reads.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Missing_tenant_is_forbidden()
    {
        _user.SetupGet(u => u.TenantId).Returns(Guid.Empty);

        (await EmployeeHandler().Handle(new GetLedEmployeeHoursQuery(), default)).StatusCode.Should().Be(403);
        (await TaskHandler().Handle(new GetLedProgressTasksQuery(), default)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Caller_without_legal_entity_gets_an_empty_page()
    {
        _caller.LegalEntityId = null;

        var employees = await EmployeeHandler().Handle(new GetLedEmployeeHoursQuery(), default);
        var tasks = await TaskHandler().Handle(new GetLedProgressTasksQuery(), default);

        employees.IsSuccess.Should().BeTrue();
        employees.Value!.Items.Should().BeEmpty();
        employees.Value.TotalCount.Should().Be(0);
        employees.Value.Unassigned.TotalHours.Should().Be(0);
        tasks.Value!.Items.Should().BeEmpty();
        _reads.VerifyNoOtherCalls();
        _leadership.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Caller_with_no_led_modules_gets_an_empty_page()
    {
        _leadership.Setup(l => l.ResolveLedScopeAsync(_tenantId, _caller.Id, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LedWorkScope.Empty);

        var employees = await EmployeeHandler().Handle(new GetLedEmployeeHoursQuery(), default);
        var tasks = await TaskHandler().Handle(new GetLedProgressTasksQuery(), default);

        employees.Value!.Items.Should().BeEmpty();
        employees.Value.TotalPages.Should().Be(0);
        tasks.Value!.Items.Should().BeEmpty();
        tasks.Value.HasNext.Should().BeFalse();
        _reads.VerifyNoOtherCalls();
    }

    // ---------- scope narrowing ----------

    [Fact]
    public async Task Out_of_scope_project_or_objective_gives_an_empty_page_without_querying()
    {
        var foreign = Guid.NewGuid();

        (await EmployeeHandler().Handle(new GetLedEmployeeHoursQuery(ProjectId: foreign), default)).Value!.Items.Should().BeEmpty();
        (await EmployeeHandler().Handle(new GetLedEmployeeHoursQuery(ObjectiveId: foreign), default)).Value!.Items.Should().BeEmpty();
        (await TaskHandler().Handle(new GetLedProgressTasksQuery(ProjectId: foreign), default)).Value!.Items.Should().BeEmpty();
        (await TaskHandler().Handle(new GetLedProgressTasksQuery(ObjectiveId: foreign), default)).Value!.Items.Should().BeEmpty();
        // A led module that belongs to a different project than the one asked for is out of scope too.
        (await TaskHandler().Handle(new GetLedProgressTasksQuery(ProjectId: _p2, ObjectiveId: _b), default)).Value!.Items.Should().BeEmpty();

        _reads.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task No_filter_passes_the_whole_led_scope()
    {
        await EmployeeHandler().Handle(new GetLedEmployeeHoursQuery(), default);
        await TaskHandler().Handle(new GetLedProgressTasksQuery(), default);

        _employeeFilter!.ObjectiveIds.Should().BeEquivalentTo(new[] { _a, _b, _c, _d });
        _unassignedIds.Should().BeEquivalentTo(new[] { _a, _b, _c, _d });
        _taskFilter!.ObjectiveIds.Should().BeEquivalentTo(new[] { _a, _b, _c, _d });
    }

    [Fact]
    public async Task Project_filter_keeps_only_that_projects_modules()
    {
        await EmployeeHandler().Handle(new GetLedEmployeeHoursQuery(ProjectId: _p2), default);
        await TaskHandler().Handle(new GetLedProgressTasksQuery(ProjectId: _p1), default);

        _employeeFilter!.ObjectiveIds.Should().BeEquivalentTo(new[] { _d });
        _unassignedIds.Should().BeEquivalentTo(new[] { _d });
        _taskFilter!.ObjectiveIds.Should().BeEquivalentTo(new[] { _a, _b, _c });
    }

    [Fact]
    public async Task Objective_filter_expands_to_its_subtree_inside_the_scope()
    {
        await EmployeeHandler().Handle(new GetLedEmployeeHoursQuery(ObjectiveId: _b), default);
        await TaskHandler().Handle(new GetLedProgressTasksQuery(ProjectId: _p1, ObjectiveId: _c), default);

        _employeeFilter!.ObjectiveIds.Should().BeEquivalentTo(new[] { _b, _c });
        _unassignedIds.Should().BeEquivalentTo(new[] { _b, _c });
        _taskFilter!.ObjectiveIds.Should().BeEquivalentTo(new[] { _c });
    }

    [Fact]
    public void Scope_filter_never_reaches_a_descendant_the_scope_dropped()
    {
        // B's child C is in the tree rows but not accessible (not in SubtreeObjectiveIds).
        var end = new DateOnly(2026, 12, 31);
        var scope = new LedWorkScope(
        [
            new LedHeadModule(_a, _p1, "A", false, end, new HashSet<Guid> { _a, _b }, 0,
                [new(_a, _p1, null, "A", false, end), new(_b, _p1, _a, "B", false, end), new(_c, _p1, _b, "C", false, end)]),
        ], []);

        LedScopeFilter.Resolve(scope, null, _a).Should().BeEquivalentTo(new[] { _a, _b });
        LedScopeFilter.Resolve(scope, null, _c).Should().BeEmpty();
    }

    // ---------- paging, search, sort, filters reaching the repository ----------

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-5, 500, 1, 100)]
    [InlineData(3, 20, 3, 20)]
    [InlineData(2, 101, 2, 100)]
    public async Task Paging_is_clamped(int pageNumber, int pageSize, int expectedPage, int expectedSize)
    {
        var employees = await EmployeeHandler().Handle(new GetLedEmployeeHoursQuery(pageNumber, pageSize), default);
        var tasks = await TaskHandler().Handle(new GetLedProgressTasksQuery(pageNumber, pageSize), default);

        employees.Value!.PageNumber.Should().Be(expectedPage);
        employees.Value.PageSize.Should().Be(expectedSize);
        _employeeFilter!.Take.Should().Be(expectedSize);
        _employeeFilter.Skip.Should().Be((expectedPage - 1) * expectedSize);
        tasks.Value!.PageNumber.Should().Be(expectedPage);
        tasks.Value.PageSize.Should().Be(expectedSize);
        _taskFilter!.Take.Should().Be(expectedSize);
        _taskFilter.Skip.Should().Be((expectedPage - 1) * expectedSize);
    }

    [Fact]
    public async Task Employee_query_defaults_to_total_hours_descending_and_trims_search()
    {
        await EmployeeHandler().Handle(new GetLedEmployeeHoursQuery(Search: "  ada  "), default);

        _employeeFilter!.Sort.Should().Be(LedEmployeeHoursSort.TotalHours);
        _employeeFilter.Descending.Should().BeTrue();
        _employeeFilter.Search.Should().Be("ada");
    }

    [Fact]
    public async Task Employee_query_parses_sort_and_direction_case_insensitively()
    {
        await EmployeeHandler().Handle(new GetLedEmployeeHoursQuery(Sort: "NAME", Direction: "Asc", Search: "   "), default);

        _employeeFilter!.Sort.Should().Be(LedEmployeeHoursSort.Name);
        _employeeFilter.Descending.Should().BeFalse();
        _employeeFilter.Search.Should().BeNull();
    }

    [Fact]
    public async Task Employee_page_maps_rows_totals_and_unassigned()
    {
        var result = await EmployeeHandler().Handle(new GetLedEmployeeHoursQuery(PageSize: 20), default);

        var page = result.Value!;
        page.TotalCount.Should().Be(41);
        page.TotalPages.Should().Be(3);
        page.HasNext.Should().BeTrue();
        page.HasPrevious.Should().BeFalse();
        var item = page.Items.Should().ContainSingle().Subject;
        item.DisplayName.Should().Be("Ada Lovelace");
        (item.CompletedHours, item.InProgressHours, item.NotStartedHours, item.TotalHours).Should().Be((5m, 3m, 2m, 10m));
        (item.TaskCount, item.SharedTaskCount, item.SharedHours).Should().Be((3, 1, 3m));
        page.Unassigned.Should().Be(new ONEVO.Application.Features.WorkManagement.Leadership.DTOs.LedUnassignedHours(1, 2, 3, 6, 4));
    }

    [Fact]
    public async Task Task_query_passes_status_employee_sort_and_direction()
    {
        var employeeId = Guid.NewGuid();

        await TaskHandler().Handle(new GetLedProgressTasksQuery(
            Status: "inProgress", EmployeeId: employeeId, Sort: "dueDate", Direction: "asc", Search: "web"), default);

        _taskFilter!.Status.Should().Be(LedTaskBucket.InProgress);
        _taskFilter.EmployeeId.Should().Be(employeeId);
        _taskFilter.Sort.Should().Be(LedProgressTaskSort.DueDate);
        _taskFilter.Descending.Should().BeFalse();
        _taskFilter.Search.Should().Be("web");
    }

    [Fact]
    public async Task Task_query_defaults_to_estimated_hours_descending_and_no_status()
    {
        await TaskHandler().Handle(new GetLedProgressTasksQuery(), default);

        _taskFilter!.Status.Should().BeNull();
        _taskFilter.EmployeeId.Should().BeNull();
        _taskFilter.Sort.Should().Be(LedProgressTaskSort.EstimatedHours);
        _taskFilter.Descending.Should().BeTrue();
    }

    [Fact]
    public async Task Task_page_maps_wire_status_and_shared_flag()
    {
        var result = await TaskHandler().Handle(new GetLedProgressTasksQuery(), default);

        var items = result.Value!.Items;
        items.Select(i => i.Status).Should().Equal("inProgress", "completed");
        items.Select(i => i.IsShared).Should().Equal(true, false);
        items[0].Assignees.Select(a => a.DisplayName).Should().Equal("Ada", "Bob");
        items[1].Assignees.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(2);
    }

    // ---------- period ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("all")]
    [InlineData("ALL")]
    public async Task All_or_missing_period_applies_no_due_window(string? period)
    {
        await EmployeeHandler().Handle(new GetLedEmployeeHoursQuery(Period: period), default);
        await TaskHandler().Handle(new GetLedProgressTasksQuery(Period: period), default);

        _employeeFilter!.DueWindow.Should().BeNull();
        _unassignedWindow.Should().BeNull();
        _taskFilter!.DueWindow.Should().BeNull();
    }

    [Theory]
    [InlineData("week", "2026-10-19", "2026-10-25")]
    [InlineData("Week", "2026-10-19", "2026-10-25")]
    [InlineData("MONTH", "2026-10-01", "2026-10-31")]
    public async Task Week_or_month_period_passes_the_utc_window_to_every_read(string period, string from, string to)
    {
        var expected = new LedDueWindow(DateOnly.Parse(from), DateOnly.Parse(to));

        await EmployeeHandler().Handle(new GetLedEmployeeHoursQuery(Period: period), default);
        await TaskHandler().Handle(new GetLedProgressTasksQuery(Period: period), default);

        _employeeFilter!.DueWindow.Should().Be(expected);
        _unassignedWindow.Should().Be(expected, "the unassigned totals follow the same window");
        _taskFilter!.DueWindow.Should().Be(expected);
    }

    [Fact]
    public async Task Period_window_uses_the_utc_date_not_the_offset_local_date()
    {
        // 2026-10-26 01:00 at +05:30 is still Sunday 2026-10-25 in UTC -> week of 19 Oct.
        _clock.UtcNow = new DateTimeOffset(2026, 10, 26, 1, 0, 0, TimeSpan.FromHours(5.5));

        await TaskHandler().Handle(new GetLedProgressTasksQuery(Period: "week"), default);

        _taskFilter!.DueWindow.Should().Be(new LedDueWindow(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 25)));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("all", true)]
    [InlineData("Week", true)]
    [InlineData(" month ", true)]
    [InlineData("year", false)]
    [InlineData("today", false)]
    public void Detail_validators_accept_only_known_periods(string? period, bool valid)
    {
        new GetLedEmployeeHoursQueryValidator().Validate(new GetLedEmployeeHoursQuery(Period: period)).IsValid.Should().Be(valid);
        new GetLedProgressTasksQueryValidator().Validate(new GetLedProgressTasksQuery(Period: period)).IsValid.Should().Be(valid);
    }

    // ---------- validators ----------

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("totalHours", "desc", true)]
    [InlineData("notstartedhours", "ASC", true)]
    [InlineData("name", "", true)]
    [InlineData("bogus", "desc", false)]
    [InlineData("totalHours", "sideways", false)]
    public void Employee_validator_accepts_only_known_sort_and_direction(string? sort, string? direction, bool valid)
        => new GetLedEmployeeHoursQueryValidator().Validate(new GetLedEmployeeHoursQuery(Sort: sort, Direction: direction))
            .IsValid.Should().Be(valid);

    [Theory]
    [InlineData(null, null, null, true)]
    [InlineData("completed", "status", "asc", true)]
    [InlineData("notstarted", "TITLE", "desc", true)]
    [InlineData("done", null, null, false)]
    [InlineData(null, "priority", null, false)]
    [InlineData(null, null, "up", false)]
    public void Task_validator_accepts_only_known_status_sort_and_direction(string? status, string? sort, string? direction, bool valid)
        => new GetLedProgressTasksQueryValidator().Validate(new GetLedProgressTasksQuery(Status: status, Sort: sort, Direction: direction))
            .IsValid.Should().Be(valid);

    [Fact]
    public void Validators_reject_an_over_long_search()
    {
        var search = new string('x', LedProgressQueryOptions.MaxSearchLength + 1);
        new GetLedEmployeeHoursQueryValidator().Validate(new GetLedEmployeeHoursQuery(Search: search)).IsValid.Should().BeFalse();
        new GetLedProgressTasksQueryValidator().Validate(new GetLedProgressTasksQuery(Search: search)).IsValid.Should().BeFalse();
    }
}
