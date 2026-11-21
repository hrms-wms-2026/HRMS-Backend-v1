using Microsoft.AspNetCore.Authorization;

namespace ONEVO.Api.Extensions;

internal static class AuthorizationExtensions
{
    internal static IServiceCollection AddApiAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy("TenantPolicy", policy =>
                policy.AddAuthenticationSchemes("TenantScheme")
                      .RequireAuthenticatedUser());

            // AdminPolicy (every admin/v1 route) and the narrow MFA-enrollment policy.
            // See AdminAuthorizationPolicies for the F-02 mandatory-MFA rule.
            ONEVO.Api.Auth.AdminAuthorizationPolicies.Register(options);

            options.AddPolicy("TrayDevicePolicy", policy =>
                policy.AddAuthenticationSchemes("TrayDeviceScheme")
                      .RequireAuthenticatedUser()
                      .RequireClaim("token_type", "tray_device"));

            // Task 15: deny by default. Any endpoint with no [Authorize], policy or [AllowAnonymous]
            // requires a signed-in tenant user, admin or tray device (whichever scheme matches the
            // request). Public endpoints must opt out with [AllowAnonymous] / AllowAnonymous().
            // ControllerAuthorizationCoverageArchitectureTests keeps every controller action explicit.
            options.FallbackPolicy = BuildFallbackPolicy();
        });

        return services;
    }

    internal static AuthorizationPolicy BuildFallbackPolicy() =>
        new AuthorizationPolicyBuilder(ONEVO.Api.Auth.FallbackAuthenticationScheme.Name)
            .RequireAuthenticatedUser()
            .Build();
}
