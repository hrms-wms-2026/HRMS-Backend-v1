using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Api.Auth;
using ONEVO.Api.Controllers.Admin.DevPlatform.Auth;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;

namespace ONEVO.Tests.Unit.Api.Authorization;

/// <summary>
/// F-02: an admin who has not enrolled MFA holds an enrollment-only session. AdminPolicy (used by
/// every admin/v1 route) must refuse it; only the four enrollment endpoints use the narrower policy.
/// </summary>
public sealed class AdminAuthorizationPoliciesTests
{
    private static IAuthorizationService BuildAuthorizationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(AdminAuthorizationPolicies.Register);
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal AdminPrincipal(string? mfaEnrolledClaim, bool withRole = true)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        if (withRole)
            claims.Add(new Claim("platform_role", "Platform Super Admin"));
        if (mfaEnrolledClaim is not null)
            claims.Add(new Claim(PlatformMfaClaims.EnrolledClaimType, mfaEnrolledClaim));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "AdminScheme"));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    [InlineData("TRUE", false)]
    public async Task AdminPolicy_RequiresEnrolledMfa(string? claimValue, bool expected)
    {
        var result = await BuildAuthorizationService()
            .AuthorizeAsync(AdminPrincipal(claimValue), AdminAuthorizationPolicies.Admin);

        result.Succeeded.Should().Be(expected);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData(null)]
    public async Task MfaEnrollmentPolicy_AllowsAnyAuthenticatedAdmin_WhateverTheEnrollmentState(string? claimValue)
    {
        var result = await BuildAuthorizationService()
            .AuthorizeAsync(AdminPrincipal(claimValue), AdminAuthorizationPolicies.MfaEnrollment);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task BothPolicies_RejectUnauthenticatedAndRolelessPrincipals()
    {
        var service = BuildAuthorizationService();
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        (await service.AuthorizeAsync(anonymous, AdminAuthorizationPolicies.Admin)).Succeeded.Should().BeFalse();
        (await service.AuthorizeAsync(anonymous, AdminAuthorizationPolicies.MfaEnrollment)).Succeeded.Should().BeFalse();
        (await service.AuthorizeAsync(AdminPrincipal("false", withRole: false), AdminAuthorizationPolicies.MfaEnrollment))
            .Succeeded.Should().BeFalse();
    }

    [Fact]
    public void MfaEnrollmentPolicy_IsUsedOnlyByTheFourEnrollmentEndpoints()
    {
        var apiAssembly = typeof(AdminAuthController).Assembly;
        var users = new List<string>();

        foreach (var controller in apiAssembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t)))
        {
            if (UsesPolicy(controller, AdminAuthorizationPolicies.MfaEnrollment))
                users.Add(controller.Name);

            foreach (var action in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (UsesPolicy(action, AdminAuthorizationPolicies.MfaEnrollment))
                    users.Add($"{controller.Name}.{action.Name}");
            }
        }

        users.Should().BeEquivalentTo(
            "AdminAuthController.EnableMfa",
            "AdminAuthController.ConfirmMfaSetup",
            "AdminAuthController.Me",
            "AdminAuthController.Logout");
    }

    [Fact]
    public void AdminAuthController_EveryAuthenticatedActionDeclaresExactlyOneKnownPolicy()
    {
        var actions = typeof(AdminAuthController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());

        foreach (var action in actions)
        {
            if (action.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                continue;

            var policies = action.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).ToList();
            policies.Should().ContainSingle(because: $"{action.Name} must declare one admin policy")
                .Which.Should().BeOneOf(AdminAuthorizationPolicies.Admin, AdminAuthorizationPolicies.MfaEnrollment);
        }
    }

    private static bool UsesPolicy(MemberInfo member, string policy) =>
        member.GetCustomAttributes<AuthorizeAttribute>(inherit: false).Any(a => a.Policy == policy);
}
