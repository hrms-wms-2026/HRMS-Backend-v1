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

    [Fact]
    public async Task Hidden_action_center_leaves_no_hole_after_compaction()
    {
        var d = Seed(DashboardKeys.MyWork);
        Allow(DashboardKeys.MyWork, PlainEmployeeWork);

        var result = await Handler().Handle(new GetDashboardBootstrapQuery(d.Id, "desktop"), default);

        var byKey = result.Value!.Widgets.ToDictionary(w => w.WidgetKey);
        Assert.Equal(4, byKey["todays-schedule"].Layout.Y);
        Assert.Equal(0, byKey["today-total-time"].Layout.Y);
        _repo.Verify(r => r.SaveWithVersionAsync(d, 1, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(2, result.Value.LayoutVersion);
    }

    [Fact]
    public async Task Hidden_widget_that_becomes_allowed_is_reinserted_without_overlap()
    {
        var d = Seed(DashboardKeys.MyWork);
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
