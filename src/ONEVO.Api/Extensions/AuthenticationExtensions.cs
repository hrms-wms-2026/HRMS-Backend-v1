using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ONEVO.Infrastructure.Identity.Sessions;
using ONEVO.Infrastructure.Services.Monitoring.TrayActivation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ONEVO.Api.Extensions;

internal static class AuthenticationExtensions
{
    internal static IServiceCollection AddApiAuthentication(
        this IServiceCollection services, IWebHostEnvironment env, IConfiguration configuration)
    {
        services.AddSingleton<TenantDatabaseTicketStore>();
        services.AddSingleton<AdminDatabaseTicketStore>();

        // Tenant session exchange (see TenantSessionExchangeService) always completes the real
        // sign-in on the tenant host itself, so crownus_session stays host-scoped (no Domain) -
        // there is no cross-subdomain cookie sharing to support.
        services.AddAuthentication("TenantScheme")
            .AddCookie("TenantScheme", options =>
            {
                options.Cookie.Name = ONEVO.Api.Auth.AuthCookieNames.Session;
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
                        type = ONEVO.Api.Errors.ProblemTypes.For("unauthorized"),
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
                        type = ONEVO.Api.Errors.ProblemTypes.For("forbidden"),
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
                // Unlike TenantScheme, the admin frontend always lives on a different subdomain
                // (admin.<domain>) than this shared API host - a host-only cookie (no Domain) is
                // invisible to admin_csrf's document.cookie read on that page even though it's
                // still auto-attached to requests here, which silently breaks every CSRF-guarded
                // mutation. AdminCookieDomain lets deployments opt into subdomain-shared cookies;
                // unset (the default) preserves today's host-only behavior.
                var adminCookieDomain = configuration["AdminCookieDomain"];
                if (!string.IsNullOrWhiteSpace(adminCookieDomain))
                    options.Cookie.Domain = adminCookieDomain;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = ONEVO.Application.Common.Models.Auth.SessionPolicy.SlidingWindow;
                // SessionStore is set via Options configuration below

                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return context.Response.WriteAsJsonAsync(new
                    {
                        type = ONEVO.Api.Errors.ProblemTypes.For("unauthorized"),
                        title = "Unauthorized",
                        status = 401,
                        detail = "Authentication is required to access this resource.",
                        correlationId = context.HttpContext.Items["X-Correlation-Id"]?.ToString() ?? Guid.NewGuid().ToString()
                    });
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    // F-02: an admin without confirmed MFA is refused by AdminPolicy. Say so, so the
                    // admin console can send them to MFA setup instead of a generic "no permission".
                    var awaitingMfa = ONEVO.Api.Auth.AdminAuthorizationPolicies.IsAwaitingMfaEnrollment(context.HttpContext.User);
                    return context.Response.WriteAsJsonAsync(new
                    {
                        type = ONEVO.Api.Errors.ProblemTypes.For(awaitingMfa ? "mfa_enrollment_required" : "forbidden"),
                        title = "Forbidden",
                        status = 403,
                        detail = awaitingMfa
                            ? "Set up two-factor authentication to use the admin console."
                            : "You do not have permission to access this resource.",
                        correlationId = context.HttpContext.Items["X-Correlation-Id"]?.ToString() ?? Guid.NewGuid().ToString()
                    });
                };
            })
            .AddJwtBearer("TrayDeviceScheme", options =>
            {
                var trayJwt = configuration.GetSection("TrayApp:Jwt");

                var issuer = trayJwt["Issuer"] ?? "crownus-tray";
                var audience = trayJwt["Audience"] ?? "crownus-tray-app";

                // Keys, algorithms and kid matching are set from ITrayJwtKeyProvider below
                // (SEC-15), so the issuer and this validator share one key set.
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                    // MapInboundClaims is intentionally left at its default (true) so that
                    // the JWT 'sub' claim is mapped to ClaimTypes.NameIdentifier, which
                    // TrayCurrentDeviceService relies on to resolve DeviceRegistrationId.
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken)
                            && context.HttpContext.Request.Path.StartsWithSegments("/hubs/agent-commands"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json";
                        return context.Response.WriteAsJsonAsync(new
                        {
                            type = ONEVO.Api.Errors.ProblemTypes.For("unauthorized"),
                            title = "Unauthorized",
                            status = 401,
                            detail = "A valid tray device token is required to access this resource.",
                            correlationId = context.HttpContext.Items["X-Correlation-Id"]?.ToString() ?? Guid.NewGuid().ToString()
                        });
                    }
                };
            })
            // Task 15: used only by the authorization fallback policy. Forwards to the one scheme
            // that matches the request; see FallbackAuthenticationScheme.
            .AddPolicyScheme(ONEVO.Api.Auth.FallbackAuthenticationScheme.Name, displayName: null, options =>
                options.ForwardDefaultSelector = ONEVO.Api.Auth.FallbackAuthenticationScheme.Select);

        // SEC-15: ES256 only (HS256 too while a legacy window is configured), and a token's kid must
        // name a listed key. TryAllIssuerSigningKeys stays off: with it on, a token signed by a listed
        // key would validate under any made-up kid.
        services.AddOptions<JwtBearerOptions>("TrayDeviceScheme")
            .Configure<ITrayJwtKeyProvider>((options, keys) =>
            {
                var parameters = options.TokenValidationParameters;
                parameters.ValidAlgorithms = keys.ValidAlgorithms;
                parameters.TryAllIssuerSigningKeys = false;
                parameters.IssuerSigningKeyResolver = (_, token, kid, _) => keys.ResolveValidationKeys(token, kid);
            });

        services.AddOptions<CookieAuthenticationOptions>("TenantScheme")
            .Configure<TenantDatabaseTicketStore>((options, store) => options.SessionStore = store);

        services.AddOptions<CookieAuthenticationOptions>("AdminScheme")
            .Configure<AdminDatabaseTicketStore>((options, store) => options.SessionStore = store);

        return services;
    }
}
