using Microsoft.AspNetCore.Authentication.Cookies;
using ONEVO.Infrastructure.Identity.Sessions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ONEVO.Api.Extensions;

internal static class AuthenticationExtensions
{
    internal static IServiceCollection AddApiAuthentication(
        this IServiceCollection services, IWebHostEnvironment env)
    {
        services.AddSingleton<TenantDatabaseTicketStore>();
        services.AddSingleton<AdminDatabaseTicketStore>();

        // Tenant session exchange (see TenantSessionExchangeService) always completes the real
        // sign-in on the tenant host itself, so onevo_session stays host-scoped (no Domain) -
        // there is no cross-subdomain cookie sharing to support.
        services.AddAuthentication("TenantScheme")
            .AddCookie("TenantScheme", options =>
            {
                options.Cookie.Name = "onevo_session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = env.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = ONEVO.Application.Common.Models.Auth.SessionPolicy.SlidingWindow;
                // SessionStore is set via Options configuration below

                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return context.Response.WriteAsJsonAsync(new
                    {
                        type = "https://onevo.com/errors/unauthorized",
                        title = "Unauthorized",
                        status = 401,
                        detail = "Authentication is required to access this resource.",
                        correlationId = context.HttpContext.Items["X-Correlation-Id"]?.ToString() ?? Guid.NewGuid().ToString()
                    });
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return context.Response.WriteAsJsonAsync(new
                    {
                        type = "https://onevo.com/errors/forbidden",
                        title = "Forbidden",
                        status = 403,
                        detail = "You do not have permission to access this resource.",
                        correlationId = context.HttpContext.Items["X-Correlation-Id"]?.ToString() ?? Guid.NewGuid().ToString()
                    });
                };
            })
            .AddCookie("AdminScheme", options =>
            {
                options.Cookie.Name = "admin_session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = env.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = ONEVO.Application.Common.Models.Auth.SessionPolicy.SlidingWindow;
                // SessionStore is set via Options configuration below

                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return context.Response.WriteAsJsonAsync(new
                    {
                        type = "https://onevo.com/errors/unauthorized",
                        title = "Unauthorized",
                        status = 401,
                        detail = "Authentication is required to access this resource.",
                        correlationId = context.HttpContext.Items["X-Correlation-Id"]?.ToString() ?? Guid.NewGuid().ToString()
                    });
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return context.Response.WriteAsJsonAsync(new
                    {
                        type = "https://onevo.com/errors/forbidden",
                        title = "Forbidden",
                        status = 403,
                        detail = "You do not have permission to access this resource.",
                        correlationId = context.HttpContext.Items["X-Correlation-Id"]?.ToString() ?? Guid.NewGuid().ToString()
                    });
                };
            });

        services.AddOptions<CookieAuthenticationOptions>("TenantScheme")
            .Configure<TenantDatabaseTicketStore>((options, store) => options.SessionStore = store);

        services.AddOptions<CookieAuthenticationOptions>("AdminScheme")
            .Configure<AdminDatabaseTicketStore>((options, store) => options.SessionStore = store);

        return services;
    }
}
