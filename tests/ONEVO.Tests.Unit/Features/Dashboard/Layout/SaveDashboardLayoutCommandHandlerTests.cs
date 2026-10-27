using ONEVO.Application.Features.Dashboard.Layout.Commands.SaveDashboardLayout;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Domain.Features.Dashboard.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Layout;

public sealed class SaveDashboardLayoutCommandHandlerTests
{
    private readonly DashboardMutationFixture _f = new();

    private SaveDashboardLayoutCommandHandler Handler() => new(_f.Repo.Object, _f.Access.Object, _f.User.Object);

    private static List<LayoutItemDto> Current(UserDashboard d, string bp) =>
        d.Widgets.Where(w => w.WidgetKey != "action-center")
            .Select(w => { var r = DashboardMutationFixture.Rect(w, bp); return new LayoutItemDto(w.Id, r.X, r.Y, r.W, r.H); })
            .ToList();

    [Fact]
    public async Task Saves_layout_and_bumps_version()
    {
        var d = _f.Seed(DashboardKeys.MyWork, version: 2);
        var items = Current(d, "desktop");
        // wellbeing-free swap: move work-pattern to the right-hand free area is not needed; resize quick-actions.
        var qa = d.Widgets.Single(w => w.WidgetKey == "work-pattern");
        items = items.Select(i => i.InstanceId == qa.Id ? i with { H = 5 } : i).ToList();

        var result = await Handler().Handle(new SaveDashboardLayoutCommand(d.Id, "desktop", 2, items), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value);
        Assert.Equal(5, DashboardMutationFixture.Rect(qa, "desktop").H);
    }

    [Fact]
    public async Task Stale_version_conflicts()
    {
        var d = _f.Seed(DashboardKeys.MyWork, version: 7);

        var result = await Handler().Handle(new SaveDashboardLayoutCommand(d.Id, "desktop", 6, Current(d, "desktop")), default);

        Assert.Equal(409, result.StatusCode);
        Assert.Contains("currentVersion=7", result.Error);
    }

    [Fact]
    public async Task Foreign_dashboard_not_found()
    {
        var d = _f.Seed(DashboardKeys.MyWork, owner: Guid.NewGuid());

        var result = await Handler().Handle(new SaveDashboardLayoutCommand(d.Id, "desktop", 1, []), default);

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Unknown_breakpoint_rejected()
    {
        var d = _f.Seed(DashboardKeys.MyWork);

        var result = await Handler().Handle(new SaveDashboardLayoutCommand(d.Id, "watch", 1, Current(d, "desktop")), default);

        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Overlap_rejected()
    {
        var d = _f.Seed(DashboardKeys.MyWork);
        var items = Current(d, "desktop");
        var a = items[0];
        items[1] = items[1] with { X = a.X, Y = a.Y };

        var result = await Handler().Handle(new SaveDashboardLayoutCommand(d.Id, "desktop", 1, items), default);

        Assert.Equal(422, result.StatusCode);
        Assert.Contains("OVERLAP", result.Error);
    }

    [Fact]
    public async Task Missing_visible_instance_rejected()
    {
        var d = _f.Seed(DashboardKeys.MyWork);
        var items = Current(d, "desktop").Skip(1).ToList();

        var result = await Handler().Handle(new SaveDashboardLayoutCommand(d.Id, "desktop", 1, items), default);

        Assert.Equal(422, result.StatusCode);
        Assert.Equal("LAYOUT_SET_MISMATCH", result.Error);
    }

    [Fact]
    public async Task Hidden_instance_in_payload_rejected()
    {
        var d = _f.Seed(DashboardKeys.MyWork);
        var hidden = d.Widgets.Single(w => w.WidgetKey == "action-center");
        var items = Current(d, "desktop");
        items.Add(new LayoutItemDto(hidden.Id, 0, 40, 4, 3));

        var result = await Handler().Handle(new SaveDashboardLayoutCommand(d.Id, "desktop", 1, items), default);

        Assert.Equal(422, result.StatusCode);
        Assert.Equal("LAYOUT_SET_MISMATCH", result.Error);
    }

    [Fact]
    public async Task Hidden_instance_untouched()
    {
        var d = _f.Seed(DashboardKeys.MyWork);
        var hidden = d.Widgets.Single(w => w.WidgetKey == "action-center");
        var before = DashboardMutationFixture.Rect(hidden, "desktop");
        var items = Current(d, "desktop").Select(i => i with { Y = i.Y + 20 }).ToList();

        var result = await Handler().Handle(new SaveDashboardLayoutCommand(d.Id, "desktop", 1, items), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(before, DashboardMutationFixture.Rect(hidden, "desktop"));
    }

    [Fact]
    public async Task Below_min_size_rejected()
    {
        var d = _f.Seed(DashboardKeys.MyWork);
        var items = Current(d, "desktop");
        items[0] = items[0] with { W = 1, H = 1 };

        var result = await Handler().Handle(new SaveDashboardLayoutCommand(d.Id, "desktop", 1, items), default);

        Assert.Equal(422, result.StatusCode);
        Assert.Contains("SIZE_NOT_ALLOWED", result.Error);
    }

    [Fact]
    public async Task Rejected_save_leaves_layout_unchanged()
    {
        var d = _f.Seed(DashboardKeys.MyWork);
        var items = Current(d, "desktop");
        items[0] = items[0] with { W = 1, H = 1 };
        var before = DashboardMutationFixture.Rect(d.Widgets.Single(w => w.Id == items[0].InstanceId), "desktop");

        await Handler().Handle(new SaveDashboardLayoutCommand(d.Id, "desktop", 1, items), default);

        Assert.Equal(before, DashboardMutationFixture.Rect(d.Widgets.Single(w => w.Id == items[0].InstanceId), "desktop"));
        Assert.Equal(1, d.LayoutVersion);
    }
}
