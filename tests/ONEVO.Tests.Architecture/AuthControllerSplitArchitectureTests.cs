using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Controllers.Tenant.Auth;

namespace ONEVO.Tests.Architecture;

public sealed class AuthControllerSplitArchitectureTests
{
    private static readonly Type[] TenantAuthControllers =
    {
        typeof(AuthInvitationController),
        typeof(AuthLoginController),
        typeof(AuthMfaController),
        typeof(AuthPasswordController),
        typeof(AuthSessionController),
        typeof(AuthPendingLegalController)
    };

    [Fact]
    public void NoTenantAuthController_HasMoreThanFivePublicActions()
    {
        foreach (var controllerType in TenantAuthControllers)
        {
            var actionCount = controllerType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Count(m => !m.IsSpecialName);

            actionCount.Should().BeLessThanOrEqualTo(5,
                $"{controllerType.Name} must stay focused on one responsibility (max 5 public actions)");
        }
    }

    [Fact]
    public void OldFlatAuthControllerNoLongerExists()
    {
        var apiAssembly = typeof(AuthLoginController).Assembly;
        apiAssembly.GetType("ONEVO.Api.Controllers.Tenant.Auth.AuthController").Should().BeNull(
            "the oversized flat AuthController must be deleted after the split");
    }

    [Fact]
    public void AllOriginalAuthRoutesStillResolve()
    {
        var expectedRoutes = new (Type Controller, string Method, string FullPath)[]
        {
            (typeof(AuthInvitationController), "GET", "api/v1/auth/invitations/{token}"),
            (typeof(AuthInvitationController), "POST", "api/v1/auth/invitations/{token}/accept-password"),
            (typeof(AuthInvitationController), "POST", "api/v1/auth/invitations/{token}/accept-google"),
            (typeof(AuthLoginController), "POST", "api/v1/auth/login"),
            (typeof(AuthLoginController), "POST", "api/v1/auth/login/select-workspace"),
            (typeof(AuthLoginController), "POST", "api/v1/auth/login/google"),
            (typeof(AuthMfaController), "POST", "api/v1/auth/mfa/enable"),
            (typeof(AuthMfaController), "POST", "api/v1/auth/mfa/verify"),
            (typeof(AuthMfaController), "POST", "api/v1/auth/mfa/confirm-setup"),
            (typeof(AuthPasswordController), "POST", "api/v1/auth/forgot-password"),
            (typeof(AuthPasswordController), "POST", "api/v1/auth/reset-password"),
            (typeof(AuthPasswordController), "POST", "api/v1/auth/force-change-password"),
            (typeof(AuthSessionController), "GET", "api/v1/auth/me"),
            (typeof(AuthSessionController), "POST", "api/v1/auth/logout"),
            (typeof(AuthPendingLegalController), "POST", "api/v1/legal/acceptances/complete-login")
        };

        foreach (var (controller, method, fullPath) in expectedRoutes)
        {
            var classPrefix = controller.GetCustomAttribute<RouteAttribute>()?.Template;
            var matched = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(m => method == "GET"
                    ? m.GetCustomAttributes<HttpGetAttribute>().Select(a => a.Template)
                    : m.GetCustomAttributes<HttpPostAttribute>().Select(a => a.Template))
                .Any(template =>
                {
                    var resolved = string.IsNullOrEmpty(template)
                        ? classPrefix
                        : template.StartsWith("/")
                            ? template.TrimStart('/')
                            : $"{classPrefix}/{template}";
                    return string.Equals(resolved, fullPath, StringComparison.OrdinalIgnoreCase);
                });

            matched.Should().BeTrue($"{controller.Name} must still expose {method} {fullPath}");
        }
    }
}
