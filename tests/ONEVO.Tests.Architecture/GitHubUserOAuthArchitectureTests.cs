using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Controllers.Tenant.Integrations;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using Xunit;

namespace ONEVO.Tests.Architecture;

public sealed class GitHubUserOAuthArchitectureTests
{
    [Fact]
    public void Own_user_endpoints_use_tenant_policy_without_integration_permissions()
    {
        var type = typeof(GitHubIntegrationController);
        Assert.Contains(
            type.GetCustomAttributes<AuthorizeAttribute>(),
            attribute => attribute.Policy == "TenantPolicy");
        Assert.Equal(
            "api/v1/integrations/github",
            type.GetCustomAttribute<RouteAttribute>()!.Template);

        foreach (var methodName in new[] { "Start", "Callback", "Status", "Refresh", "Disconnect" })
        {
            var method = type.GetMethod(methodName)!;
            Assert.Empty(method.GetCustomAttributes<RequirePermissionAttribute>());
            Assert.Empty(method.GetCustomAttributes<RequirePlatformPermissionAttribute>());
        }
    }

    [Theory]
    [InlineData("TenantStatus")]
    [InlineData("EnableTenant")]
    [InlineData("DisableTenant")]
    public void Tenant_admin_endpoints_require_integrations_manage(string methodName)
    {
        var method = typeof(GitHubIntegrationController).GetMethod(methodName)!;
        var permission = Assert.Single(
            method.GetCustomAttributes<RequirePermissionAttribute>());
        var field = typeof(RequirePermissionAttribute).GetField(
            "_permission",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal("integrations:manage", field.GetValue(permission));
        Assert.Empty(method.GetCustomAttributes<RequirePlatformPermissionAttribute>());
    }

    [Fact]
    public void Controller_does_not_depend_on_secret_resolver()
    {
        var dependencies = typeof(GitHubIntegrationController).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType);

        Assert.DoesNotContain(typeof(IPlatformOAuthAppResolver), dependencies);
    }

    [Fact]
    public void Safe_responses_have_no_secret_or_credential_value_fields()
    {
        var types = new[]
        {
            typeof(GitHubOAuthStartResponse),
            typeof(GitHubOAuthCompleteResponse),
            typeof(GitHubTenantApprovalDto),
            typeof(UserIntegrationConnectionDto)
        };
        var forbidden = new[]
        {
            "AccessToken",
            "RefreshToken",
            "Encrypted",
            "ClientSecret",
            "PrivateKey",
            "AuthorizationCode",
            "RawResponse"
        };

        foreach (var property in types.SelectMany(type => type.GetProperties()))
        {
            Assert.DoesNotContain(
                forbidden,
                value => property.Name.Contains(value, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Application_handlers_have_no_ef_context_dependency()
    {
        var assembly = typeof(UserIntegrationConnectionDto).Assembly;
        var handlers = assembly.GetTypes().Where(type =>
            type.Namespace?.Contains(
                "SharedPlatform.TenantIntegrations",
                StringComparison.Ordinal) == true &&
            type.Name.EndsWith("Handler", StringComparison.Ordinal));
        var parameters = handlers
            .SelectMany(handler => handler.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters());

        Assert.DoesNotContain(parameters, parameter =>
            (parameter.ParameterType.FullName ?? string.Empty)
                .Contains("DbContext", StringComparison.Ordinal));
    }
}
