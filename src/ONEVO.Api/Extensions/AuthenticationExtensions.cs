using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using ONEVO.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace ONEVO.Api.Extensions;

internal static class AuthenticationExtensions
{
    internal static IServiceCollection AddApiAuthentication(
        this IServiceCollection services,
        IWebHostEnvironment env,
        IConfiguration configuration)
    {
        services.AddSingleton<TenantDatabaseTicketStore>();
        services.AddSingleton<AdminDatabaseTicketStore>();

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
            })
            .AddJwtBearer("AgentScheme", options =>
            {
                var secret = configuration["Jwt:AgentSecret"]
                    ?? throw new InvalidOperationException("Jwt:AgentSecret is required.");
                var issuer = configuration["Jwt:AgentIssuer"] ?? "onevo";

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = "onevo-agent",
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };

                options.Events = new JwtBearerEvents
                {
                    OnChallenge = context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return context.Response.WriteAsJsonAsync(new
                        {
                            type = "https://onevo.com/errors/unauthorized",
                            title = "Unauthorized",
                            status = 401,
                            detail = "A valid device token is required."
                        });
                    }
                };
            });

        services.AddOptions<CookieAuthenticationOptions>("TenantScheme")
            .Configure<TenantDatabaseTicketStore>((options, store) => options.SessionStore = store);

        services.AddOptions<CookieAuthenticationOptions>("AdminScheme")
            .Configure<AdminDatabaseTicketStore>((options, store) => options.SessionStore = store);

        return services;
    }
}
