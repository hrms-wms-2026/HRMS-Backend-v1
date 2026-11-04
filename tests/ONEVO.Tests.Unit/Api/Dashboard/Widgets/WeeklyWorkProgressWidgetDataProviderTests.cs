using Moq;
using ONEVO.Api.Controllers.Tenant.WorkManagement;
using ONEVO.Api.Dashboard.Widgets;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Application.Features.WorkManagement.WeeklyProgress.DTOs;
using ONEVO.Application.Features.WorkManagement.WeeklyProgress.Queries.GetMyWeeklyWorkProgress;
using Xunit;

namespace ONEVO.Tests.Unit.Api.Dashboard.Widgets;

public sealed class WeeklyWorkProgressWidgetDataProviderTests
{
    private static WeeklyWorkProgressWidgetDataProvider Create(WidgetProviderTestKit kit) =>
        new(kit.Mediator.Object, kit.User.Object, kit.Entitlements.Object);

    private static WeekProgressDto Week() => new(2700, 1350, 0, 0, 0, 120, 60, 180, 480, 2520, 6.7m, 0m, null, "behind", []);

    [Fact]
    public void Provides_the_today_total_time_widget()
    {
        Assert.Equal("today-total-time", WeeklyWorkProgressWidgetDataProvider.Key);
        Assert.True(DashboardWidgetRegistry.Get(WeeklyWorkProgressWidgetDataProvider.Key)!.HasProvider);
    }

    [Fact]
    public async Task Works_without_work_management_modules()
    {
        // Every My Work user gets this widget; like the planner, the endpoint has no module gate (own data only), so an
        // HR-only tenant still sees its attendance-based week.
        var kit = new WidgetProviderTestKit(activeModules: []);
        kit.Mediator.Setup(m => m.Send(It.IsAny<GetMyWeeklyWorkProgressQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WeeklyWorkProgressResponse>.NotFound("Current employee record was not found."));

        var result = await Create(kit).GetAsync(default);

        Assert.Equal(404, result.StatusCode);
        kit.Mediator.Verify(m => m.Send(It.IsAny<GetMyWeeklyWorkProgressQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Returns_same_view_model_as_endpoint_for_the_current_week()
    {
        var kit = new WidgetProviderTestKit(activeModules: ["tasks"]);
        var response = new WeeklyWorkProgressResponse(new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 1), new DateOnly(2026, 10, 28),
            "Asia/Colombo", false, Week(), Week(), new WeekTaskWeightsDto(3, 5, 2, 1));
        kit.Mediator.Setup(m => m.Send(It.IsAny<GetMyWeeklyWorkProgressQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WeeklyWorkProgressResponse>.Success(response));

        var provided = await Create(kit).GetAsync(default);
        var endpoint = await new MyWorkProgressController(kit.Mediator.Object).MyWeeklyProgress(null, default);

        WidgetProviderTestKit.AssertSameBody(endpoint, provided);
        kit.Mediator.Verify(m => m.Send(It.Is<GetMyWeeklyWorkProgressQuery>(q => q.WeekStart == null), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Handler_failure_keeps_its_status()
    {
        var kit = new WidgetProviderTestKit(activeModules: ["tasks"]);
        kit.Mediator.Setup(m => m.Send(It.IsAny<GetMyWeeklyWorkProgressQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WeeklyWorkProgressResponse>.NotFound("Current employee record was not found."));

        var result = await Create(kit).GetAsync(default);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
