using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using Xunit;

namespace ONEVO.Tests.Architecture;

public sealed class IntegrationCatalogArchitectureTests
{
    [Fact]
    public void Controller_is_admin_authorized_and_uses_platform_permissions()
    {
        var type = typeof(IntegrationCatalogController);
        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(), x => x.Policy == "AdminPolicy");
        Assert.Equal("admin/v1/system-config/integrations", type.GetCustomAttribute<RouteAttribute>()!.Template);
        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            if (method.IsSpecialName) continue;
            var permission = method.GetCustomAttribute<RequirePlatformPermissionAttribute>();
            Assert.NotNull(permission);
            Assert.Contains(permission!.Permission, new[] { PlatformPermissionCatalog.SystemConfigRead, PlatformPermissionCatalog.SystemConfigManage });
        }
    }

    [Fact]
    public void Application_handlers_do_not_depend_on_ef_core()
    {
        var assembly = typeof(ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Helpers.IntegrationCatalogRules).Assembly;
        var handlers = assembly.GetTypes().Where(x => x.Namespace?.Contains("SystemConfig.IntegrationCatalog") == true && x.Name.EndsWith("Handler"));
        foreach (var parameter in handlers.SelectMany(x => x.GetConstructors()).SelectMany(x => x.GetParameters()))
            Assert.DoesNotContain("DbContext", parameter.ParameterType.FullName ?? string.Empty);
    }
}
