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
using System.Security.Claims;

DotEnvLoader.LoadIfPresent();

var builder = WebApplication.CreateBuilder(args);

ConfigurationStartupValidator.ValidateRequiredLocalConfiguration(
    builder.Configuration,
    builder.Environment.EnvironmentName);
// Outside Development/Test: refuse to start on a missing, placeholder or local-host setting (task 05).
ONEVO.Infrastructure.Configuration.DeploymentConfigurationValidator.Validate(
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

// Task 17: OpenTelemetry when an exporter is configured (docs/deployment/observability.md). Serilog then also
// forwards its events to the OpenTelemetry log provider.
var telemetryExporter = builder.AddApiObservability();
builder.Host.UseSerilog((ctx, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .Enrich.WithProperty("Application", "ONEVO.Api");
    // Readable lines locally; one JSON object per line elsewhere, so the host's log store can query the fields.
    if (ctx.HostingEnvironment.IsDevelopment())
        cfg.WriteTo.Console();
    else
        cfg.WriteTo.Console(new Serilog.Formatting.Compact.RenderedCompactJsonFormatter());
}, writeToProviders: telemetryExporter != ObservabilityExtensions.TelemetryExporter.None);

builder.Services.AddApiForwardedHeaders(builder.Configuration);

builder.Services.AddApplication();
builder.Services.Configure<ONEVO.Application.Features.Monitoring.TrayActivation.Options.TrayPresenceOptions>(
    builder.Configuration.GetSection("TrayPresence"));
builder.Services.Configure<ONEVO.Api.Configuration.TrayReleasesOptions>(
    builder.Configuration.GetSection(ONEVO.Api.Configuration.TrayReleasesOptions.SectionName));

builder.Services.AddInfrastructure(builder.Configuration);
ONEVO.Api.Dashboard.Widgets.WidgetDataProviderRegistration.AddDashboardWidgetDataProviders(builder.Services);
builder.Services.AddMemoryCache();
builder.Services.AddControllers()
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
builder.Services.AddApiSwagger();
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
    }, ["ready"])
    // Task 17: Degraded (still 200) when storage or the outbox is in trouble; see the check classes for why.
    .AddCheck<ONEVO.Infrastructure.Observability.ObjectStorageHealthCheck>("object-storage", tags: ["ready"])
    .AddCheck<ONEVO.Infrastructure.Observability.OutboxBacklogHealthCheck>("outbox", tags: ["ready"]);
builder.Services.AddSingleton<ONEVO.Infrastructure.Observability.OutboxBacklogProbe>();
builder.Services.AddSingleton<ONEVO.Infrastructure.Observability.IOutboxBacklogSource>(
    sp => sp.GetRequiredService<ONEVO.Infrastructure.Observability.OutboxBacklogProbe>());

var app = builder.Build();
// Creating the probe registers the crownus.outbox.* gauges, so they report without waiting for a readiness call.
app.Services.GetRequiredService<ONEVO.Infrastructure.Observability.OutboxBacklogProbe>();

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
app.UseApiSwagger();
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
app.MapHub<ONEVO.Api.Hubs.AgentCommandsHub>("/hubs/agent-commands",
    options => options.CloseOnAuthenticationExpiration = true);
app.MapHub<ONEVO.Api.Hubs.InboxHub>("/hubs/inbox");
// Liveness: process-only checks. Must NOT include dependency checks — a brief
// DB outage should fail readiness, not liveness.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => !check.Tags.Contains("ready")
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapSpaHostingFallbacks();

app.Run();

public partial class Program { }
