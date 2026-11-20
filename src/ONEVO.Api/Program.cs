using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using ONEVO.Api.Configuration;
using ONEVO.Api.Extensions;
using ONEVO.Api.Middleware;
using ONEVO.Api.RateLimiting;
using ONEVO.Application;
using ONEVO.Application.Common.Json;
using ONEVO.Infrastructure;
using ONEVO.Infrastructure.Configuration;
using Serilog;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Security.Claims;

DotEnvLoader.LoadIfPresent();

var builder = WebApplication.CreateBuilder(args);

// Task 23: `dotnet build` runs this file up to builder.Build() to write the OpenAPI documents
// (Microsoft.Extensions.ApiDescription.Server). That must work with no database and no secrets, so
// the startup checks below are skipped in that mode only. The documents need nothing but routing.
var generatingOpenApiDocuments = OpenApiDocumentExtensions.IsGeneratingDocuments;
if (generatingOpenApiDocuments)
    builder.Configuration.AddInMemoryCollection(OpenApiDocumentExtensions.GenerationPlaceholderSettings);

if (!generatingOpenApiDocuments)
{
    ConfigurationStartupValidator.ValidateRequiredLocalConfiguration(
        builder.Configuration,
        builder.Environment.EnvironmentName);
    ConfigurationStartupValidator.ValidateTrayJwtSigningKeys(
        builder.Configuration,
        builder.Environment.EnvironmentName);

    // Development only: generates the local mkcert certificate if it's missing, before anything
    // that depends on the database, so a fresh clone doesn't need a separate manual cert-setup
    // step. Production/Staging terminate TLS with a real certificate authority.
    if (builder.Environment.IsDevelopment())
    {
        DevCertificateBootstrapper.EnsureCertificateExists(
            builder.Configuration,
            builder.Environment.ContentRootPath);
    }

    await DatabaseConnectionStartupValidator.ValidateAndOpenAsync(
        builder.Configuration,
        builder.Environment.EnvironmentName,
        DotEnvLoader.DefaultConnectionProcessOverrideActive
            || DotEnvLoader.MigrationConnectionProcessOverrideActive);

    await ONEVO.Infrastructure.Configuration.DatabaseSettingsStartupValidator.ValidateAsync(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        CancellationToken.None);

    // Development only: Test bootstraps its own Testcontainers database via
    // IntegrationDatabaseBootstrap before this process starts, and Production/Staging must apply
    // migrations through their own deployment pipeline, never automatically on process start.
    if (builder.Environment.IsDevelopment())
    {
        await DatabaseMigrationRunner.MigrateIfPendingAsync(builder.Configuration);
    }
}

builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .Enrich.WithProperty("Application", "ONEVO.Api"));

builder.Services.AddApiForwardedHeaders(builder.Configuration);

// Task 23: every tenant lives on a subdomain and every subdomain is HTTPS, so HSTS covers them all
// for a year. No preload: that is a separate, hard-to-undo decision. UseHsts stays off in Development.
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = false;
});

// Task 23: server-wide backstop for the 1 MB default body limit. RequestBodySizeLimitMiddleware
// raises it per request for endpoints that declare [RequestSizeLimit] (uploads).
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = RequestBodySizeLimitMiddleware.DefaultMaxBodyBytes);

builder.Services.AddApplication();
builder.Services.Configure<ONEVO.Application.Features.Monitoring.TrayActivation.Options.TrayPresenceOptions>(
    builder.Configuration.GetSection("TrayPresence"));
builder.Services.Configure<ONEVO.Api.Configuration.TrayReleasesOptions>(
    builder.Configuration.GetSection(ONEVO.Api.Configuration.TrayReleasesOptions.SectionName));

builder.Services.AddInfrastructure(builder.Configuration);
ONEVO.Api.Dashboard.Widgets.WidgetDataProviderRegistration.AddDashboardWidgetDataProviders(builder.Services);
builder.Services.AddMemoryCache();
builder.Services.AddControllers(options =>
        // Task 23: every file response is a download with nosniff and a sandbox CSP.
        options.Filters.Add<ONEVO.Api.Filters.UserFileResponseFilter>())
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new TimeOnlyHhMmJsonConverter()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddApiAuthentication(builder.Environment, builder.Configuration);
builder.Services.AddApiAuthorization();
builder.Services.AddSignalR();
builder.Services.AddSingleton<
    ONEVO.Application.Features.Monitoring.Settings.ServiceInterfaces.ITrayPolicyRefreshNotifier,
    ONEVO.Api.Hubs.SignalRTrayPolicyRefreshNotifier>();
// Inbox realtime: replaces Infrastructure's null publisher with the InboxHub-backed one.
Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.Replace(
    builder.Services,
    ServiceDescriptor.Scoped<
        ONEVO.Application.Features.Conversations.Contracts.IInboxRealtimePublisher,
        ONEVO.Api.Hubs.SignalRInboxRealtimePublisher>());
builder.Services.AddSingleton<
    ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces.ITrayPushNotifier,
    ONEVO.Api.Hubs.SignalRTrayPushNotifier>();
builder.Services.AddSingleton<ONEVO.Api.Hubs.TrayConnectionTracker>();
builder.Services.AddSingleton<
    ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces.ITrayConnectionPresence>(
    sp => sp.GetRequiredService<ONEVO.Api.Hubs.TrayConnectionTracker>());
builder.Services.AddSingleton<ONEVO.Api.Middleware.WebActivityTracker>();
builder.Services.AddSingleton<
    ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces.IWebActivityPresence>(
    sp => sp.GetRequiredService<ONEVO.Api.Middleware.WebActivityTracker>());
// Task 23: no Swagger UI and no runtime /swagger or /openapi endpoint. The API description is generated
// at build time only (docs/api/openapi); nothing here is ever mapped.
builder.Services.AddApiOpenApiDocuments();
builder.Services.AddApiCors(builder.Configuration);
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck("api", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("API is running"))
    .AddAsyncCheck("sqlserver", async () =>
    {
        // Readiness must fail when the database is unreachable; liveness (/health)
        // deliberately excludes this check so a brief DB outage does not restart the app.
        try
        {
            var cs = builder.Configuration.GetConnectionString("DefaultConnection");
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cs);
            await conn.OpenAsync();
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync();
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("SQL Server reachable");
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("SQL Server unreachable", ex);
        }
    }, ["ready"]);

// Task 23: the build-time document generator starts the host, so drop the hosted services
// (seeders, background jobs) that would otherwise reach for the database.
if (generatingOpenApiDocuments)
    builder.Services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();

var app = builder.Build();

if (ONEVO.Application.Common.Tenancy.TenancyModeReader.Read(app.Configuration)
    == ONEVO.Application.Common.Tenancy.TenancyMode.SingleHost)
{
    app.Logger.LogWarning("Tenancy:Mode is SingleHost - test mode only; tenant comes from the X-Crownus-Tenant header.");
}

// First, so scheme, host and client IP are the real client's for everything below
// (HTTPS redirect, HSTS, logging, CORS, rate limiting, tenant resolution).
app.UseForwardedHeaders();
app.UseMiddleware<ExceptionHandlerMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseSerilogRequestLogging(opts =>
    opts.EnrichDiagnosticContext = (diag, ctx) =>
    {
        diag.Set("TenantId", ctx.User?.FindFirst("tenant_id")?.Value ?? "anon");
        diag.Set("UserId", ctx.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anon");
    });

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
// Optional (Hosting:ServeSpas): SPA static files are served here, after the security headers,
// HSTS and HTTPS redirect, but before routing, tenant resolution, auth, rate limiting and CSRF.
// The index.html fallback is mapped below as an anonymous endpoint.
app.UseSpaHosting();
app.UseCors("AllowFrontend");
app.UseRouting();
// Task 23: 1 MB default body limit, larger only where an endpoint declares [RequestSizeLimit].
// Before anything that authenticates or reads the body.
app.UseMiddleware<RequestBodySizeLimitMiddleware>();
// Task 07: coarse per-IP flood guard before any credential is validated.
app.UseApiPreAuthenticationRateLimiting();
app.UseMiddleware<HostTenantResolutionMiddleware>();
app.UseMiddleware<AuthRateLimitingMiddleware>();
app.UseAuthentication();
// Task 07: global limiter after authentication, so signed-in requests are partitioned by user,
// tray device and tenant instead of by IP. Limits: docs/deployment/rate-limits.md.
app.UseApiRateLimiting();
// Middleware removed as part of cookie auth migration
app.UseMiddleware<CsrfProtectionMiddleware>();
app.UseMiddleware<TenantEnforcementMiddleware>();
app.UseMiddleware<ONEVO.Api.Middleware.WebActivityTrackingMiddleware>();
app.UseMiddleware<TrayPresenceEnforcementMiddleware>();

app.UseMiddleware<PermissionVersionMiddleware>();

app.UseAuthorization();

app.MapControllers();
// Close a tray connection when its device JWT expires — the live connection counts as presence.
// Task 15: both hubs state their policy on the endpoint as well as on the hub class, so the
// fallback policy never decides who may connect.
app.MapHub<ONEVO.Api.Hubs.AgentCommandsHub>("/hubs/agent-commands",
    options => options.CloseOnAuthenticationExpiration = true)
    .RequireAuthorization("TrayDevicePolicy");
app.MapHub<ONEVO.Api.Hubs.InboxHub>("/hubs/inbox")
    .RequireAuthorization("TenantPolicy");
// Liveness: process-only checks. Must NOT include dependency checks — a brief
// DB outage should fail readiness, not liveness.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => !check.Tags.Contains("ready")
})
    // Task 15: probes for the load balancer / orchestrator; they return only Healthy/Unhealthy.
    .AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
})
    .AllowAnonymous();
app.MapSpaHostingFallbacks();

app.Run();

public partial class Program { }
