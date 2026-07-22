using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using ONEVO.Api.Configuration;
using ONEVO.Api.Extensions;
using ONEVO.Api.Middleware;
using ONEVO.Application;
using ONEVO.Infrastructure;
using ONEVO.Infrastructure.Configuration;
using Serilog;
using System.Security.Claims;

DotEnvLoader.LoadIfPresent();

var builder = WebApplication.CreateBuilder(args);

ConfigurationStartupValidator.ValidateRequiredLocalConfiguration(
    builder.Configuration,
    builder.Environment.EnvironmentName);

await DatabaseConnectionStartupValidator.ValidateAndOpenAsync(
    builder.Configuration,
    builder.Environment.EnvironmentName,
    DotEnvLoader.DefaultConnectionProcessOverrideActive
        || DotEnvLoader.MigrationConnectionProcessOverrideActive);

builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .Enrich.WithProperty("Application", "ONEVO.Api"));

builder.Services.Configure<ForwardedHeadersOptions>(options =>
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMemoryCache();
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddApiAuthentication(builder.Environment);
builder.Services.AddApiAuthorization();
builder.Services.AddApiSwagger();
builder.Services.AddApiCors(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck("api", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("API is running"))
    .AddAsyncCheck("postgres", async () =>
    {
        // Readiness must fail when the database is unreachable; liveness (/health)
        // deliberately excludes this check so a brief DB outage does not restart the app.
        try
        {
            var cs = builder.Configuration.GetConnectionString("DefaultConnection");
            await using var conn = new Npgsql.NpgsqlConnection(cs);
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync();
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("PostgreSQL reachable");
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("PostgreSQL unreachable", ex);
        }
    }, ["ready"]);

var app = builder.Build();

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
app.UseCors("AllowFrontend");
app.UseForwardedHeaders();
app.UseRouting();
app.UseMiddleware<PublicApiRateLimitingMiddleware>();
app.UseMiddleware<HostTenantResolutionMiddleware>();
app.UseMiddleware<AuthRateLimitingMiddleware>();
app.UseAuthentication();
// Middleware removed as part of cookie auth migration
app.UseMiddleware<CsrfProtectionMiddleware>();
app.UseMiddleware<TenantEnforcementMiddleware>();
app.UseMiddleware<PermissionVersionMiddleware>();
app.UseAuthorization();

app.MapControllers();
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

app.Run();

public partial class Program { }
