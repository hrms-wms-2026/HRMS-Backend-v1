using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;
using Xunit;

namespace ONEVO.Tests.Architecture;

public sealed class TenantIntegrationCredentialsArchitectureTests
{
    [Fact]
    public void Controller_has_admin_policy_and_exact_permissions()
    {
        var type = typeof(TenantIntegrationCredentialsController);
        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(), x => x.Policy == "AdminPolicy");
        Assert.Equal("admin/v1/system-config/tenant-integrations", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(PlatformPermissionCatalog.SystemConfigRead, Permission(type, "List"));
        Assert.Equal(PlatformPermissionCatalog.SystemConfigRead, Permission(type, "Get"));
        Assert.Equal(PlatformPermissionCatalog.SystemConfigManage, Permission(type, "Disconnect"));
    }

    [Fact]
    public void Safe_response_dto_has_no_token_values_or_encrypted_fields()
    {
        var names = typeof(TenantIntegrationCredentialDto).GetProperties().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(names, x => x.Equals("AccessToken", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, x => x.Equals("RefreshToken", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, x => x.Contains("Encrypted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Controller_does_not_depend_on_internal_resolver()
    {
        var dependencies = typeof(TenantIntegrationCredentialsController).GetConstructors()
            .SelectMany(x => x.GetParameters()).Select(x => x.ParameterType);
        Assert.DoesNotContain(typeof(ITenantIntegrationCredentialResolver), dependencies);
    }

    [Fact]
    public void Application_handlers_do_not_depend_on_ef_db_context()
    {
        var assembly = typeof(TenantIntegrationCredentialDto).Assembly;
        var handlers = assembly.GetTypes().Where(x =>
            x.Namespace?.Contains("SharedPlatform.TenantIntegrations") == true && x.Name.EndsWith("Handler"));
        foreach (var dependency in handlers.SelectMany(x => x.GetConstructors()).SelectMany(x => x.GetParameters()))
        {
            Assert.DoesNotContain("DbContext", dependency.ParameterType.FullName ?? string.Empty);
        }
    }

    private static string Permission(Type type, string method) =>
        type.GetMethod(method)!.GetCustomAttribute<RequirePlatformPermissionAttribute>()!.Permission;
}
