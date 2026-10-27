using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ONEVO.Api.Controllers.Tenant.Attendance;
using ONEVO.Api.Controllers.Tenant.WorkManagement;
using ONEVO.Api.Dashboard.Widgets;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Data;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using Xunit;

namespace ONEVO.Tests.Unit.Api.Dashboard.Widgets;

public sealed class EndpointGateTests
{
    [Fact]
    public void Reads_class_level_module_gate()
    {
        var gate = EndpointGate.For<TasksController>(nameof(TasksController.MyTasks));

        var modules = Assert.Single(gate.ModuleKeySets);
        Assert.Equal(WidgetProviderTestKit.WorkModules, modules);
        Assert.Empty(gate.Permissions);
    }

    [Fact]
    public void Reads_method_level_permission_gate()
    {
        var gate = EndpointGate.For<TimeTrackingController>(nameof(TimeTrackingController.TeamToday));

        Assert.Equal(["attendance:read"], gate.Permissions);
        Assert.Empty(gate.ModuleKeySets);
    }

    [Fact]
    public void Unknown_authorization_filter_is_rejected()
    {
        Assert.Throws<NotSupportedException>(() =>
            EndpointGate.For<GatedTestController>(nameof(GatedTestController.Visible)));
    }

    [Fact]
    public void Filter_factory_attribute_is_rejected()
    {
        Assert.Throws<NotSupportedException>(() =>
            EndpointGate.For<GatedTestController>(nameof(GatedTestController.FactoryFiltered)));
    }

    [Fact]
    public void Unknown_policy_is_rejected()
    {
        Assert.Throws<NotSupportedException>(() =>
            EndpointGate.For<GatedTestController>(nameof(GatedTestController.AdminOnly)));
    }

    [Fact]
    public async Task Any_permission_and_all_permissions_are_enforced()
    {
        var gate = EndpointGate.For<GatedTestController>(nameof(GatedTestController.Combined));
        async Task<bool> Allows(params string[] permissions)
        {
            var kit = new WidgetProviderTestKit(permissions, ["tasks"]);
            return await gate.AllowsAsync(kit.User.Object, kit.Entitlements.Object, default);
        }

        Assert.True(await Allows("a:read", "b:read"));
        Assert.True(await Allows("a:read", "c:read"));
        Assert.False(await Allows("b:read", "c:read"));
        Assert.False(await Allows("a:read"));
    }

    [Fact]
    public async Task Unauthenticated_user_is_not_allowed()
    {
        var gate = EndpointGate.For<TasksController>(nameof(TasksController.MyTasks));
        var kit = new WidgetProviderTestKit(activeModules: ["tasks"]);
        kit.User.SetupGet(u => u.IsAuthenticated).Returns(false);

        Assert.False(await gate.AllowsAsync(kit.User.Object, kit.Entitlements.Object, default));
    }

    [Fact]
    public async Task Module_match_is_case_insensitive_like_the_filter()
    {
        var gate = EndpointGate.For<TasksController>(nameof(TasksController.MyTasks));
        var kit = new WidgetProviderTestKit(activeModules: ["Boards"]);

        Assert.True(await gate.AllowsAsync(kit.User.Object, kit.Entitlements.Object, default));
    }

    [Fact]
    public void Registered_providers_match_registry_HasProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IMediator>());
        services.AddSingleton(Mock.Of<ICurrentUser>());
        services.AddSingleton(Mock.Of<IModuleEntitlementService>());
        services.AddDashboardWidgetDataProviders();
        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();
        var isKeyed = root.GetRequiredService<IServiceProviderIsKeyedService>();

        foreach (var key in DashboardWidgetRegistry.ForDashboard(DashboardKeys.MyWork)
                     .Concat(DashboardWidgetRegistry.ForDashboard(DashboardKeys.MyTeam)))
        {
            Assert.True(key.HasProvider == isKeyed.IsKeyedService(typeof(IWidgetDataProvider), key.Key), key.Key);
            if (key.HasProvider)
                Assert.Equal(key.Key, scope.ServiceProvider.GetRequiredKeyedService<IWidgetDataProvider>(key.Key).WidgetKey);
        }
    }

    [Fact]
    public void Provider_gates_are_built_without_error()
    {
        // Static gates resolve reflection at type initialisation; touching each provider surfaces a bad action name.
        var kit = new WidgetProviderTestKit();
        IWidgetDataProvider[] providers =
        [
            new MyTasksWidgetDataProvider(kit.Mediator.Object, kit.User.Object, kit.Entitlements.Object),
            new TeamProgressWidgetDataProvider(kit.Mediator.Object, kit.User.Object, kit.Entitlements.Object),
            new TeamStatusWidgetDataProvider(kit.Mediator.Object, kit.User.Object, kit.Entitlements.Object),
            new WellbeingWidgetDataProvider(kit.Mediator.Object, kit.User.Object, kit.Entitlements.Object),
            new ActiveProjectsWidgetDataProvider(kit.Mediator.Object, kit.User.Object, kit.Entitlements.Object),
            new TeamAttendanceWidgetDataProvider(kit.Mediator.Object, kit.User.Object, kit.Entitlements.Object),
        ];
        Assert.Equal(6, providers.Select(p => p.WidgetKey).Distinct().Count());
    }

    [Authorize(Policy = "TenantPolicy")]
    [RequireAnyPermission("b:read", "c:read")]
    private sealed class GatedTestController : ControllerBase
    {
        [HttpGet("visible/{id}")]
        [RequireVisibleEmployee]
        public IActionResult Visible() => Ok();

        [HttpGet("admin")]
        [Authorize(Policy = "AdminPolicy")]
        public IActionResult AdminOnly() => Ok();

        [HttpGet("factory")]
        [ServiceFilter(typeof(object))]
        public IActionResult FactoryFiltered() => Ok();

        [HttpGet("combined")]
        [RequirePermission("a:read")]
        public IActionResult Combined() => Ok();
    }
}
