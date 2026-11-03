using ONEVO.Application.Features.Dashboard.Layout.Queries.GetAvailableWidgets;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Layout;

public sealed class GetAvailableWidgetsQueryHandlerTests
{
    private static GetAvailableWidgetsQueryHandler Handler(DashboardMutationFixture f) =>
        new(f.Repo.Object, f.Access.Object, f.User.Object);

    [Fact]
    public async Task Lists_only_allowed_catalog_for_dashboard()
    {
        var f = new DashboardMutationFixture();
        var d = f.Seed(DashboardKeys.MyWork); // plain employee: no action-center

        var result = await Handler(f).Handle(new GetAvailableWidgetsQuery(d.Id, Breakpoints.Desktop), default);

        Assert.True(result.IsSuccess);
        var keys = result.Value!.Select(w => w.Key).ToList();
        Assert.DoesNotContain("action-center", keys);
        Assert.DoesNotContain("team-status", keys);
        Assert.Equal(DashboardMutationFixture.PlainEmployeeWork.OrderBy(k => k), keys.OrderBy(k => k));
        var glance = result.Value!.Single(w => w.Key == "today-at-a-glance");
        Assert.Equal("Today at a glance", glance.Title);
        Assert.Equal(new[] { 3, 3, 6, 8 }, new[] { glance.Size.MinW, glance.Size.MinH, glance.Size.MaxW, glance.Size.MaxH });
        Assert.Equal((4, 3), (glance.DefaultW, glance.DefaultH));
    }

    [Fact]
    public async Task Marks_placed()
    {
        var f = new DashboardMutationFixture();
        var d = f.Seed(DashboardKeys.MyWork);

        var result = await Handler(f).Handle(new GetAvailableWidgetsQuery(d.Id, Breakpoints.Desktop), default);

        var byKey = result.Value!.ToDictionary(w => w.Key);
        Assert.True(byKey["my-tasks"].Placed);
        // Catalog widget that is not in the default layout is available but not placed.
        Assert.False(byKey["notifications"].Placed);
        Assert.Equal((3, 3), (byKey["notifications"].DefaultW, byKey["notifications"].DefaultH));
    }

    [Fact]
    public async Task Sizes_are_clamped_to_breakpoint_columns()
    {
        var f = new DashboardMutationFixture();
        var d = f.Seed(DashboardKeys.MyWork);

        var result = await Handler(f).Handle(new GetAvailableWidgetsQuery(d.Id, Breakpoints.Mobile), default);

        Assert.All(result.Value!, w => Assert.True(w.Size.MaxW <= 4 && w.Size.MinW <= 4 && w.DefaultW <= 4));
        var pattern = result.Value!.Single(w => w.Key == "work-pattern");
        Assert.Equal(4, pattern.Size.MaxW);
        Assert.Equal(4, pattern.DefaultW);
    }

    [Fact]
    public async Task Unknown_breakpoint_is_unprocessable()
    {
        var f = new DashboardMutationFixture();
        var d = f.Seed(DashboardKeys.MyWork);

        var result = await Handler(f).Handle(new GetAvailableWidgetsQuery(d.Id, "watch"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Other_users_dashboard_is_not_found()
    {
        var f = new DashboardMutationFixture();
        var d = f.Seed(DashboardKeys.MyWork, owner: Guid.NewGuid());

        var result = await Handler(f).Handle(new GetAvailableWidgetsQuery(d.Id, Breakpoints.Desktop), default);

        Assert.Equal(404, result.StatusCode);
    }
}
