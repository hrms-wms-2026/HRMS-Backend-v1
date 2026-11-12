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
        });

        return services;
    }
}
