using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Access;
using ONEVO.Application.Features.Dashboard.Layout.Data;
using ONEVO.Application.Features.Dashboard.Layout.DTOs;
using ONEVO.Application.Features.Dashboard.Layout.Queries.GetWidgetData;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces;
using ONEVO.Domain.Features.Dashboard.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Layout;

public sealed class GetWidgetDataQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUserDashboardRepository> _repo = new();
    private readonly Mock<IDashboardWidgetAccess> _access = new();
    private readonly Mock<IWidgetDataOrchestrator> _orchestrator = new(MockBehavior.Strict);
    private readonly Mock<ICurrentUser> _user = new();

    private GetWidgetDataQueryHandler Handler()
    {
        _user.SetupGet(u => u.TenantId).Returns(TenantId);
        _user.SetupGet(u => u.UserId).Returns(UserId);
        _user.SetupGet(u => u.IsAuthenticated).Returns(true);
        return new GetWidgetDataQueryHandler(_repo.Object, _access.Object, _orchestrator.Object, _user.Object);
    }

    private DashboardWidget Seed(string dashboardKey, string widgetKey, Guid? owner = null)
    {
        var dashboard = new UserDashboard
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = owner ?? UserId, DashboardKey = dashboardKey, LayoutVersion = 1,
        };
        var widget = new DashboardWidget { Id = Guid.NewGuid(), TenantId = TenantId, DashboardId = dashboard.Id, WidgetKey = widgetKey };
        dashboard.Widgets.Add(new DashboardWidget { Id = Guid.NewGuid(), TenantId = TenantId, DashboardId = dashboard.Id, WidgetKey = "quick-actions" });
        dashboard.Widgets.Add(widget);
        _repo.Setup(r => r.GetByWidgetIdAsync(TenantId, widget.Id, It.IsAny<CancellationToken>())).ReturnsAsync(dashboard);
        return widget;
    }

    private void Allow(string dashboardKey, params string[] keys) =>
        _access.Setup(a => a.GetAllowedKeysAsync(dashboardKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>(keys));

    private void Outcome(string key, WidgetDataOutcome outcome) =>
        _orchestrator.Setup(o => o.LoadOneAsync(key, It.IsAny<CancellationToken>())).ReturnsAsync(outcome);

    [Fact]
    public async Task Foreign_instance_not_found()
    {
        var widget = Seed(DashboardKeys.MyWork, "my-tasks", owner: Guid.NewGuid());
        Allow(DashboardKeys.MyWork, "my-tasks");

        var result = await Handler().Handle(new GetWidgetDataQuery(widget.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Unknown_instance_not_found()
    {
        var result = await Handler().Handle(new GetWidgetDataQuery(Guid.NewGuid()), default);

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Not_allowed_widget_forbidden()
    {
        var widget = Seed(DashboardKeys.MyTeam, "team-status");
        Allow(DashboardKeys.MyTeam, "team-quick-actions");

        var result = await Handler().Handle(new GetWidgetDataQuery(widget.Id), default);

        Assert.Equal(403, result.StatusCode);
        _orchestrator.Verify(o => o.LoadOneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Widget_without_provider_is_not_found_with_code()
    {
        var widget = Seed(DashboardKeys.MyWork, "todays-schedule");
        Allow(DashboardKeys.MyWork, "todays-schedule");
        Outcome("todays-schedule", new WidgetDataOutcome(new WidgetDataStateDto(WidgetDataStatus.Client, null, null), null, null));

        var result = await Handler().Handle(new GetWidgetDataQuery(widget.Id), default);

        Assert.Equal(404, result.StatusCode);
        Assert.Equal(WidgetDataErrors.NoProvider, result.Error);
    }

    [Fact]
    public async Task Retired_widget_key_is_not_found_with_code()
    {
        var widget = Seed(DashboardKeys.MyWork, "retired-widget");
        Allow(DashboardKeys.MyWork, "my-tasks");

        var result = await Handler().Handle(new GetWidgetDataQuery(widget.Id), default);

        Assert.Equal(404, result.StatusCode);
        Assert.Equal(WidgetDataErrors.NoProvider, result.Error);
    }

    [Fact]
    public async Task Widget_hidden_on_web_has_no_provider_and_loads_nothing()
    {
        var widget = Seed(DashboardKeys.MyWork, "my-tasks");
        Allow(DashboardKeys.MyWork, "my-tasks");

        var result = await Handler().Handle(new GetWidgetDataQuery(widget.Id), default);

        Assert.Equal(404, result.StatusCode);
        Assert.Equal(WidgetDataErrors.NoProvider, result.Error);
        _orchestrator.Verify(o => o.LoadOneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Success_returns_payload()
    {
        var widget = Seed(DashboardKeys.MyWork, "active-projects");
        Allow(DashboardKeys.MyWork, "active-projects");
        var payload = new object();
        Outcome("active-projects", new WidgetDataOutcome(new WidgetDataStateDto(WidgetDataStatus.Success, Now, null), payload, null));

        var result = await Handler().Handle(new GetWidgetDataQuery(widget.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(widget.Id, result.Value!.InstanceId);
        Assert.Equal(WidgetDataStatus.Success, result.Value.Status);
        Assert.Equal(Now, result.Value.FetchedAt);
        Assert.Same(payload, result.Value.Payload);
    }

    [Theory]
    [InlineData(WidgetDataErrors.Forbidden, 403)]
    [InlineData(WidgetDataErrors.Timeout, 504)]
    [InlineData(WidgetDataErrors.Failed, 500)]
    public async Task Provider_failure_keeps_its_status(string code, int status)
    {
        var widget = Seed(DashboardKeys.MyWork, "active-projects");
        Allow(DashboardKeys.MyWork, "active-projects");
        Outcome("active-projects", new WidgetDataOutcome(new WidgetDataStateDto(WidgetDataStatus.Error, Now, code), null, status));

        var result = await Handler().Handle(new GetWidgetDataQuery(widget.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(code, result.Error);
    }

    [Fact]
    public async Task Unauthenticated_is_forbidden()
    {
        var handler = Handler();
        _user.SetupGet(u => u.IsAuthenticated).Returns(false);

        var result = await handler.Handle(new GetWidgetDataQuery(Guid.NewGuid()), default);

        Assert.Equal(403, result.StatusCode);
    }
}
