using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Reflection;
using Xunit;
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;

namespace ONEVO.Tests.Architecture;

public class ControllerArchitectureTests
{
    private static readonly ArchUnitNET.Domain.Architecture Architecture = new ArchLoader()
        .LoadAssemblies(typeof(ONEVO.Api.Controllers.Tenant.Auth.AuthLoginController).Assembly)
        .Build();

    [Fact]
    public void AdminControllers_ShouldNotUse_TenantPolicy()
    {
        var offenders = GetClassesWithAuthorizePolicy("ONEVO.Api.Controllers.Admin", "TenantPolicy");
        Assert.Empty(offenders);
    }

    [Fact]
    public void TenantControllers_ShouldNotUse_AdminPolicy()
    {
        var offenders = GetClassesWithAuthorizePolicy("ONEVO.Api.Controllers.Tenant", "AdminPolicy");
        Assert.Empty(offenders);
    }

    [Fact]
    public void AdminControllers_ShouldNotUse_RequirePermission()
    {
        var offenders = GetClassesWithAttribute("ONEVO.Api.Controllers.Admin", "RequirePermissionAttribute");
        Assert.Empty(offenders);
    }

    [Fact]
    public void TenantControllers_ShouldNotUse_RequirePlatformPermission()
    {
        var offenders = GetClassesWithAttribute("ONEVO.Api.Controllers.Tenant", "RequirePlatformPermissionAttribute");
        Assert.Empty(offenders);
    }

    [Fact]
    public void NoDevPlatformControllers_OutsideAdmin()
    {
        var apiAssembly = typeof(ONEVO.Api.Controllers.Tenant.Auth.AuthLoginController).Assembly;
        var devPlatformTypes = apiAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains(".Controllers.") && t.Namespace.Contains(".DevPlatform"))
            .ToList();

        var outsideAdmin = devPlatformTypes.Where(t => !t.Namespace!.Contains(".Controllers.Admin.")).ToList();
        
        Assert.Empty(outsideAdmin.Select(t => t.FullName));
    }

    [Fact]
    public void NoAdminV1FeatureBucket()
    {
        var apiAssembly = typeof(ONEVO.Api.Controllers.Tenant.Auth.AuthLoginController).Assembly;
        var v1Types = apiAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains(".Controllers.Admin.V1"))
            .ToList();
            
        Assert.Empty(v1Types.Select(t => t.FullName));
    }

    [Fact]
    public void NoLooseAdminFeatureControllers_DirectlyUnderAdmin()
    {
        var apiAssembly = typeof(ONEVO.Api.Controllers.Tenant.Auth.AuthLoginController).Assembly;
        var adminDirectTypes = apiAssembly.GetTypes()
            .Where(t => t.Namespace == "ONEVO.Api.Controllers.Admin" && t.IsSubclassOf(typeof(ControllerBase)))
            .ToList();

        // Allowed transition files if any
        var allowed = new string[] { };
        
        var offenders = adminDirectTypes.Where(t => !allowed.Contains(t.Name)).ToList();
        
        Assert.Empty(offenders.Select(t => t.FullName));
    }

    [Fact]
    public void TenantAuthControllers_ShouldNotExpose_FindWorkspaceRoute()
    {
        var apiAssembly = typeof(ONEVO.Api.Controllers.Tenant.Auth.AuthLoginController).Assembly;
        var routes = apiAssembly.GetTypes()
            .Where(t => t.Namespace == "ONEVO.Api.Controllers.Tenant.Auth")
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetCustomAttributes<HttpPostAttribute>())
            .Select(attribute => attribute.Template);

        Assert.DoesNotContain(routes, route =>
            string.Equals(route, "find-workspace", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TenantLoginRequest_ShouldContainOnly_EmailAndPassword()
    {
        var propertyNames = typeof(ONEVO.Api.Contracts.Auth.LoginRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(new[] { "Email", "Password" }, propertyNames);
    }

    [Fact]
    public void TenantAuthControllers_ShouldExpose_CookieSessionRoutesWithoutRefresh()
    {
        var loginController = typeof(ONEVO.Api.Controllers.Tenant.Auth.AuthLoginController);
        var sessionController = typeof(ONEVO.Api.Controllers.Tenant.Auth.AuthSessionController);

        Assert.Equal("login", loginController.GetMethod("Login")!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("logout", sessionController.GetMethod("Logout")!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("me", sessionController.GetMethod("Me")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Null(loginController.GetMethod("Refresh"));
        Assert.Null(sessionController.GetMethod("Refresh"));

        var apiAssembly = loginController.Assembly;
        var postRoutes = apiAssembly.GetTypes()
            .Where(t => t.Namespace == "ONEVO.Api.Controllers.Tenant.Auth")
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetCustomAttributes<HttpPostAttribute>())
            .Select(attribute => attribute.Template);

        Assert.DoesNotContain(postRoutes, route =>
            string.Equals(route, "refresh", StringComparison.OrdinalIgnoreCase));
    }

    private static System.Collections.Generic.IEnumerable<string> GetClassesWithAuthorizePolicy(string baseNamespace, string policy)
    {
        var apiAssembly = typeof(ONEVO.Api.Controllers.Tenant.Auth.AuthLoginController).Assembly;
        var controllerTypes = apiAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.StartsWith(baseNamespace) && t.IsSubclassOf(typeof(ControllerBase)));

        foreach (var type in controllerTypes)
        {
            var hasPolicy = type.GetCustomAttributesData()
                .Any(a => a.AttributeType.Name == "AuthorizeAttribute" && 
                          a.NamedArguments.Any(na => na.MemberName == "Policy" && (string?)na.TypedValue.Value == policy));

            if (hasPolicy) yield return type.FullName!;

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                var methodHasPolicy = method.GetCustomAttributesData()
                    .Any(a => a.AttributeType.Name == "AuthorizeAttribute" && 
                              a.NamedArguments.Any(na => na.MemberName == "Policy" && (string?)na.TypedValue.Value == policy));
                if (methodHasPolicy) yield return $"{type.FullName}.{method.Name}";
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<string> GetClassesWithAttribute(string baseNamespace, string attributeName)
    {
        var apiAssembly = typeof(ONEVO.Api.Controllers.Tenant.Auth.AuthLoginController).Assembly;
        var controllerTypes = apiAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.StartsWith(baseNamespace) && t.IsSubclassOf(typeof(ControllerBase)));

        foreach (var type in controllerTypes)
        {
            if (type.GetCustomAttributesData().Any(a => a.AttributeType.Name == attributeName))
                yield return type.FullName!;

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                if (method.GetCustomAttributesData().Any(a => a.AttributeType.Name == attributeName))
                    yield return $"{type.FullName}.{method.Name}";
            }
        }
    }
}
