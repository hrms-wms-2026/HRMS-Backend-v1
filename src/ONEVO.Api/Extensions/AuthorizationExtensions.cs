using Microsoft.AspNetCore.Authorization;
using ONEVO.Api.Authorization;

namespace ONEVO.Api.Extensions;

internal static class AuthorizationExtensions
{
    internal static IServiceCollection AddApiAuthorization(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, ActiveAgentAuthorizationHandler>();

        services.AddAuthorization(options =>
        {
            options.AddPolicy("TenantPolicy", policy =>
                policy.AddAuthenticationSchemes("TenantScheme")
                      .RequireAuthenticatedUser());

            // AdminPolicy authenticates the admin cookie session. Endpoint-level
            // authorization is permission-based via [RequirePlatformPermission]
            // (platform permission codes resolved from the database); role names
            // are never authorization rules per the access-control contract.
            options.AddPolicy("AdminPolicy", policy =>
                policy.AddAuthenticationSchemes("AdminScheme")
                      .RequireAuthenticatedUser()
                      .RequireClaim("platform_role"));

            options.AddPolicy("AgentPolicy", policy =>
                policy.AddAuthenticationSchemes("AgentScheme")
                      .RequireAuthenticatedUser()
                      .RequireClaim("type", "agent"));

            options.AddPolicy("ActiveAgentPolicy", policy =>
                policy.AddAuthenticationSchemes("AgentScheme")
                      .RequireAuthenticatedUser()
                      .RequireClaim("type", "agent")
                      .AddRequirements(new ActiveAgentRequirement()));
        });

        return services;
    }
}
