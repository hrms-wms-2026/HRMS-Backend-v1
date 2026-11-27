using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using ONEVO.Api.Controllers.Admin.DevPlatform.Subscriptions;
using ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;
using ONEVO.Api.Controllers.Admin.DevPlatform.Tenants;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Improvements task 47: every rate limit route is admin-only, reads need system_config.read and
/// writes system_config.manage (plan and tenant routes included), and nothing can reach the auth
/// limits or the master switch.
/// </summary>
public sealed class RateLimitAdminApiArchitectureTests
{
    public static TheoryData<Type, string> Controllers() => new()
    {
        { typeof(RateLimitsController), "admin/v1/system-config/rate-limits" },
        { typeof(SubscriptionPlanRateLimitsController), "admin/v1/subscription-plans/{id:guid}/rate-limits" },
        { typeof(AdminTenantRateLimitsController), "admin/v1/tenants/{tenantId:guid}/rate-limits" },
    };

    [Theory]
    [MemberData(nameof(Controllers))]
    public void Controller_is_admin_only_with_system_config_permissions(Type controller, string route)
    {
        Assert.Contains(controller.GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == "AdminPolicy");
        Assert.Equal(route, controller.GetCustomAttribute<RouteAttribute>()!.Template);

        var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();
        Assert.NotEmpty(actions);

        foreach (var action in actions)
        {
            var verbs = action.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods).ToList();
            var expected = verbs.All(v => v == "GET")
                ? PlatformPermissionCatalog.SystemConfigRead
                : PlatformPermissionCatalog.SystemConfigManage;
            var permission = action.GetCustomAttribute<RequirePlatformPermissionAttribute>();
            Assert.True(permission is not null, $"{controller.Name}.{action.Name} has no RequirePlatformPermission.");
            Assert.Equal(expected, permission!.Permission);
            Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>());

            foreach (var template in action.GetCustomAttributes<HttpMethodAttribute>().Select(a => a.Template ?? ""))
                Assert.DoesNotContain("{action", template, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Limit_names_cannot_reach_auth_limits_or_the_master_switch()
    {
        var names = Enum.GetNames<RateLimitName>();
        Assert.Equal(6, names.Length);
        foreach (var forbidden in new[] { "Enabled", "PreAuthentication", "Login", "Mfa", "PasswordReset" })
            Assert.DoesNotContain(names, n => n.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void No_request_or_response_type_carries_the_master_switch()
    {
        var types = typeof(SetRateLimitRequest).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith(
                "ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits", StringComparison.Ordinal) == true);
        foreach (var type in types)
            Assert.DoesNotContain(type.GetProperties(), p => p.Name == "Enabled");
    }

    [Fact]
    public void Rate_limit_handlers_do_not_depend_on_the_db_context()
    {
        var handlers = typeof(SetRateLimitRequest).Assembly.GetTypes().Where(t =>
            t.Namespace?.Contains("SystemConfig.RateLimits", StringComparison.Ordinal) == true
            && (t.Name.EndsWith("Handler", StringComparison.Ordinal) || t.Name.EndsWith("Service", StringComparison.Ordinal)));
        foreach (var parameter in handlers.SelectMany(t => t.GetConstructors()).SelectMany(c => c.GetParameters()))
            Assert.DoesNotContain("DbContext", parameter.ParameterType.FullName ?? string.Empty);
    }
}
