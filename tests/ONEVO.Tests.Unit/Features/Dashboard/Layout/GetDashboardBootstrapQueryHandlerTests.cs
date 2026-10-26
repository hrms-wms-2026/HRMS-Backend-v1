using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Access;
using ONEVO.Application.Features.Dashboard.Layout.Data;
using ONEVO.Application.Features.Dashboard.Layout.DTOs;
using ONEVO.Application.Features.Dashboard.Layout.Grid;
using ONEVO.Application.Features.Dashboard.Layout.Queries.GetDashboardBootstrap;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Services;
using ONEVO.Domain.Features.Dashboard.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Layout;

public sealed class GetDashboardBootstrapQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly string[] PlainEmployeeWork =
        DashboardWidgetRegistry.ForDashboard(DashboardKeys.MyWork).Select(d => d.Key).Where(k => k != "action-center").ToArray();

    private readonly Mock<IUserDashboardRepository> _repo = new();
    private readonly Mock<IDashboardWidgetAccess> _access = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IWidgetDataOrchestrator> _orchestrator = new();
    private readonly List<IReadOnlyList<string>> _loadedKeys = [];
    private readonly Dictionary<string, (WidgetDataStateDto State, object? Data)> _widgetData = new();

    private GetDashboardBootstrapQueryHandler Handler()
    {
        _user.SetupGet(u => u.TenantId).Returns(TenantId);
        _user.SetupGet(u => u.UserId).Returns(UserId);
        _user.SetupGet(u => u.IsAuthenticated).Returns(true);
        _orchestrator
            .Setup(o => o.LoadAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> keys, CancellationToken _) =>
            {
                _loadedKeys.Add(keys);
                return keys.Distinct().ToDictionary(
                    k => k,
                    k => _widgetData.TryGetValue(k, out var v) ? v : (new WidgetDataStateDto("client", null, null), (object?)null));
            });
        return new GetDashboardBootstrapQueryHandler(_repo.Object, _access.Object, _user.Object, _orchestrator.Object);
    }

    private UserDashboard Seed(string key, long version = 1, Guid? owner = null)
    {
        var d = new UserDashboard
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = owner ?? UserId, DashboardKey = key, LayoutVersion = version,
            Widgets = DashboardSeeder.BuildDefaultWidgets(TenantId, key),
        };
        foreach (var w in d.Widgets)
        {
            w.Id = Guid.NewGuid();
            w.DashboardId = d.Id;
            foreach (var l in w.Layouts) l.WidgetId = w.Id;
        }
        _repo.Setup(r => r.GetByIdAsync(TenantId, d.Id, It.IsAny<CancellationToken>())).ReturnsAsync(d);
        _repo.Setup(r => r.SaveWithVersionAsync(d, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<UserDashboard, long, CancellationToken>((dd, v, _) => dd.LayoutVersion = v + 1)
            .ReturnsAsync(true);
        return d;
    }

    private void Allow(string dashboardKey, IEnumerable<string> keys) =>
        _access.Setup(a => a.GetAllowedKeysAsync(dashboardKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>(keys));

    [Fact]
    public async Task Returns_only_allowed_widgets_for_breakpoint()
    {
        var d = Seed(DashboardKeys.MyWork);
        Allow(DashboardKeys.MyWork, PlainEmployeeWork);

        var result = await Handler().Handle(new GetDashboardBootstrapQuery(d.Id, "tablet"), default);

        Assert.True(result.IsSuccess);
        var dto = result.Value!;
        Assert.DoesNotContain(dto.Widgets, w => w.WidgetKey == "action-center");
        Assert.Equal(7, dto.Widgets.Count);
        Assert.Equal(new GridSpecDto("tablet", 6, 96, 12), dto.Grid);
        Assert.All(dto.Widgets, w => Assert.True(w.Layout.X + w.Layout.W <= 6));
        Assert.All(dto.Widgets, w => Assert.True(w.Size.MaxW <= 6 && w.Size.MinW <= 6));
        Assert.All(dto.Widgets, w =>
        {
            Assert.Equal("client", w.DataState.Status);
            Assert.Null(w.Data);
        });
    }

    [Fact]
    public async Task Fills_widget_data_from_orchestrator_for_visible_widgets_only()
    {
        var d = Seed(DashboardKeys.MyWork);
        Allow(DashboardKeys.MyWork, PlainEmployeeWork);
        var payload = new object();
        var fetchedAt = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
        _widgetData["my-tasks"] = (new WidgetDataStateDto("success", fetchedAt, null), payload);
        _widgetData["active-projects"] = (new WidgetDataStateDto("error", fetchedAt, "FORBIDDEN"), null);

        var result = await Handler().Handle(new GetDashboardBootstrapQuery(d.Id, "desktop"), default);

        var keys = Assert.Single(_loadedKeys);
        Assert.DoesNotContain("action-center", keys);
        Assert.Equal(result.Value!.Widgets.Select(w => w.WidgetKey).OrderBy(k => k), keys.OrderBy(k => k));
        var tasks = Assert.Single(result.Value.Widgets, w => w.WidgetKey == "my-tasks");
        Assert.Equal(new WidgetDataStateDto("success", fetchedAt, null), tasks.DataState);
        Assert.Same(payload, tasks.Data);
        var projects = Assert.Single(result.Value.Widgets, w => w.WidgetKey == "active-projects");
        Assert.Equal(new WidgetDataStateDto("error", fetchedAt, "FORBIDDEN"), projects.DataState);
        Assert.Null(projects.Data);
    }

    [Fact]
    public async Task Other_users_dashboard_is_not_found()
    {
        var d = Seed(DashboardKeys.MyWork, owner: Guid.NewGuid());

        var result = await Handler().Handle(new GetDashboardBootstrapQuery(d.Id, "desktop"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    private static void SetDesktop(UserDashboard d, string key, GridRect r)
    {
        var row = d.Widgets.Single(w => w.WidgetKey == key).Layouts.Single(l => l.Breakpoint == "desktop");
        row.X = r.X; row.Y = r.Y; row.W = r.W; row.H = r.H;
    }

    private static List<(string, int, int, int, int)> Snapshot(UserDashboard d) =>
        d.Widgets.SelectMany(w => w.Layouts.Select(l => (w.WidgetKey + "/" + l.Breakpoint, l.X, l.Y, l.W, l.H)))
            .OrderBy(t => t.Item1).ToList();

    [Fact]
    public async Task Plain_employee_untouched_my_work_has_no_hole()
    {
        var d = Seed(DashboardKeys.MyWork);
        Allow(DashboardKeys.MyWork, PlainEmployeeWork);

        var result = await Handler().Handle(new GetDashboardBootstrapQuery(d.Id, "desktop"), default);

        var byKey = result.Value!.Widgets.ToDictionary(w => w.WidgetKey, w => w.Layout);
        Assert.Equal(7, byKey.Count);
        Assert.Equal(new GridRect(0, 0, 3, 4), byKey["today-at-a-glance"]);
        Assert.Equal(new GridRect(3, 0, 3, 4), byKey["today-total-time"]);
        Assert.Equal(new GridRect(6, 0, 3, 4), byKey["active-projects"]);
        Assert.Equal(new GridRect(9, 0, 3, 4), byKey["quick-actions"]);
        Assert.Equal(new GridRect(0, 4, 6, 4), byKey["todays-schedule"]);
        Assert.Equal(new GridRect(6, 4, 6, 4), byKey["my-tasks"]);
        Assert.Equal(new GridRect(0, 8, 12, 4), byKey["work-pattern"]);
        for (var y = 0; y < 4; y++)
            for (var x = 0; x < 12; x++)
                Assert.Contains(byKey.Values, r => x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H);
    }

    [Theory]
    [InlineData("desktop")]
    [InlineData("tablet")]
    [InlineData("mobile")]
    public async Task Variant_is_not_persisted(string breakpoint)
    {
        var d = Seed(DashboardKeys.MyWork);
        Allow(DashboardKeys.MyWork, PlainEmployeeWork);
        var before = Snapshot(d);

        var result = await Handler().Handle(new GetDashboardBootstrapQuery(d.Id, breakpoint), default);

        var variant = DashboardWidgetRegistry.DefaultLayoutFor(DashboardKeys.MyWork, new HashSet<string>(PlainEmployeeWork), breakpoint)
            .ToDictionary(i => i.Key, i => i.Rect);
        Assert.All(result.Value!.Widgets, w => Assert.Equal(variant[w.WidgetKey], w.Layout));
        _repo.Verify(r => r.SaveWithVersionAsync(It.IsAny<UserDashboard>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(before, Snapshot(d));
        Assert.Equal(1, result.Value.LayoutVersion);
    }

    [Fact]
    public async Task Lead_without_team_status_gets_team_progress_wide()
    {
        var d = Seed(DashboardKeys.MyTeam);
        Allow(DashboardKeys.MyTeam, ["team-quick-actions", "team-progress"]);

        var result = await Handler().Handle(new GetDashboardBootstrapQuery(d.Id, "desktop"), default);

        var byKey = result.Value!.Widgets.ToDictionary(w => w.WidgetKey, w => w.Layout);
        Assert.Equal(2, byKey.Count);
        Assert.Equal(new GridRect(0, 0, 8, 6), byKey["team-progress"]);
        Assert.Equal(new GridRect(8, 0, 4, 4), byKey["team-quick-actions"]);
        _repo.Verify(r => r.SaveWithVersionAsync(It.IsAny<UserDashboard>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task My_team_without_both_gated_keeps_quick_actions_in_right_column()
    {
        var d = Seed(DashboardKeys.MyTeam);
        Allow(DashboardKeys.MyTeam, ["team-quick-actions"]);

        var result = await Handler().Handle(new GetDashboardBootstrapQuery(d.Id, "desktop"), default);

        Assert.Equal(new GridRect(8, 0, 4, 4), Assert.Single(result.Value!.Widgets).Layout);
    }

    [Fact]
    public async Task Customized_layout_keeps_stored_rects()
    {
        var d = Seed(DashboardKeys.MyWork);
        // Customized: my-tasks moved into the right half of its row.
        SetDesktop(d, "my-tasks", new GridRect(6, 8, 6, 4));
        Allow(DashboardKeys.MyWork, PlainEmployeeWork);

        var result = await Handler().Handle(new GetDashboardBootstrapQuery(d.Id, "desktop"), default);

        var byKey = result.Value!.Widgets.ToDictionary(w => w.WidgetKey, w => w.Layout);
        // Not the variant: stored columns are kept, only gravity compaction applies (as before).
        Assert.Equal(new GridRect(6, 0, 3, 4), byKey["today-total-time"]);
        Assert.Equal(new GridRect(9, 4, 3, 4), byKey["active-projects"]);
        Assert.Equal(new GridRect(6, 8, 6, 4), byKey["my-tasks"]);
        Assert.Equal(new GridRect(9, 0, 3, 4), byKey["quick-actions"]);
        _repo.Verify(r => r.SaveWithVersionAsync(d, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Action_center_returning_gets_its_default_slot_on_untouched_layout()
    {
        var d = Seed(DashboardKeys.MyWork);
        Allow(DashboardKeys.MyWork, PlainEmployeeWork);
        await Handler().Handle(new GetDashboardBootstrapQuery(d.Id, "desktop"), default);

        Allow(DashboardKeys.MyWork, PlainEmployeeWork.Append("action-center"));
        var result = await Handler().Handle(new GetDashboardBootstrapQuery(d.Id, "desktop"), default);

        var full = DashboardWidgetRegistry.DefaultLayout(DashboardKeys.MyWork, "desktop").ToDictionary(i => i.Key, i => i.Rect);
        Assert.Equal(8, result.Value!.Widgets.Count);
        Assert.Equal(new GridRect(3, 0, 6, 4), result.Value.Widgets.Single(w => w.WidgetKey == "action-center").Layout);
        Assert.All(result.Value.Widgets, w => Assert.Equal(full[w.WidgetKey], w.Layout));
    }

    [Fact]
    public async Task Hidden_widget_that_becomes_allowed_is_reinserted_without_overlap()
    {
        var d = Seed(DashboardKeys.MyWork);
        SetDesktop(d, "my-tasks", new GridRect(6, 8, 6, 4));
        Allow(DashboardKeys.MyWork, PlainEmployeeWork);
        await Handler().Handle(new GetDashboardBootstrapQuery(d.Id, "desktop"), default);

        Allow(DashboardKeys.MyWork, PlainEmployeeWork.Append("action-center"));
        var result = await Handler().Handle(new GetDashboardBootstrapQuery(d.Id, "desktop"), default);

        var rects = result.Value!.Widgets.Select(w => w.Layout).ToList();
        Assert.Equal(8, rects.Count);
        for (var i = 0; i < rects.Count; i++)
            for (var j = i + 1; j < rects.Count; j++)
                Assert.False(DashboardGrid.Overlaps(rects[i], rects[j]));
    }

    [Fact]
    public async Task Unknown_breakpoint_is_unprocessable()
    {
        var d = Seed(DashboardKeys.MyWork);

        var result = await Handler().Handle(new GetDashboardBootstrapQuery(d.Id, "watch"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task My_team_without_gated_widgets_reports_HasAllowedGatedWidgets_false()
    {
        var d = Seed(DashboardKeys.MyTeam);
        Allow(DashboardKeys.MyTeam, ["team-quick-actions"]);

        var result = await Handler().Handle(new GetDashboardBootstrapQuery(d.Id, "desktop"), default);

        Assert.False(result.Value!.HasAllowedGatedWidgets);
        Assert.Single(result.Value.Widgets);
    }

    [Fact]
    public async Task Capabilities_failure_sets_CapabilitiesUnavailable()
    {
        var d = Seed(DashboardKeys.MyTeam);
        Allow(DashboardKeys.MyTeam, ["team-quick-actions"]);
        _access.Setup(a => a.CapabilitiesUnavailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Handler().Handle(new GetDashboardBootstrapQuery(d.Id, "desktop"), default);

        Assert.True(result.Value!.CapabilitiesUnavailable);
        Assert.False(result.Value.HasAllowedGatedWidgets);
    }

    [Fact]
    public async Task Unknown_registry_key_is_returned_as_unsupported_placeholder()
    {
        var d = Seed(DashboardKeys.MyWork);
        d.Widgets.Add(new DashboardWidget
        {
            Id = Guid.NewGuid(), TenantId = TenantId, DashboardId = d.Id, WidgetKey = "retired-widget",
            Layouts = [new DashboardWidgetLayout { TenantId = TenantId, Breakpoint = "desktop", X = 0, Y = 20, W = 3, H = 3 }],
        });
        Allow(DashboardKeys.MyWork, PlainEmployeeWork);

        var result = await Handler().Handle(new GetDashboardBootstrapQuery(d.Id, "desktop"), default);

        var w = Assert.Single(result.Value!.Widgets, x => x.WidgetKey == "retired-widget");
        Assert.Equal("error", w.DataState.Status);
        Assert.Equal("UNSUPPORTED_WIDGET", w.DataState.Error);
        Assert.DoesNotContain("retired-widget", Assert.Single(_loadedKeys));
    }

    [Fact]
    public async Task Missing_layout_row_is_autoplaced_and_persisted()
    {
        var d = Seed(DashboardKeys.MyWork);
        var target = d.Widgets.Single(w => w.WidgetKey == "my-tasks");
        target.Layouts.RemoveAll(l => l.Breakpoint == "mobile");
        Allow(DashboardKeys.MyWork, PlainEmployeeWork);

        var result = await Handler().Handle(new GetDashboardBootstrapQuery(d.Id, "mobile"), default);

        Assert.Contains(result.Value!.Widgets, w => w.WidgetKey == "my-tasks");
        Assert.Contains(target.Layouts, l => l.Breakpoint == "mobile");
        _repo.Verify(r => r.SaveWithVersionAsync(d, 1, It.IsAny<CancellationToken>()), Times.Once);
    }
}
