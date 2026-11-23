using System.Reflection;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ONEVO.Application.Common.Observability;

namespace ONEVO.Api.Extensions;

/// <summary>
/// Task 17: traces, metrics and logs through OpenTelemetry. The exporter is chosen by setting, so the same build
/// works for Application Insights, any OTLP backend (Grafana, Seq, a collector) or neither:
/// <list type="bullet">
/// <item><c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> set: Azure Monitor (wins when both are set).</item>
/// <item><c>OTEL_EXPORTER_OTLP_ENDPOINT</c> set: OTLP (plus the standard <c>OTEL_EXPORTER_OTLP_*</c> settings).</item>
/// <item>Neither: nothing is registered; logs stay on the console only.</item>
/// </list>
/// Health probes are not traced. Details: docs/deployment/observability.md.
/// </summary>
public static class ObservabilityExtensions
{
    public const string AzureMonitorConnectionKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";
    public const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    public const string ServiceName = "crownus-api";

    public enum TelemetryExporter
    {
        None,
        AzureMonitor,
        Otlp,
    }

    public static TelemetryExporter SelectExporter(IConfiguration configuration)
        => !string.IsNullOrWhiteSpace(configuration[AzureMonitorConnectionKey]) ? TelemetryExporter.AzureMonitor
            : !string.IsNullOrWhiteSpace(configuration[OtlpEndpointKey]) ? TelemetryExporter.Otlp
            : TelemetryExporter.None;

    /// <summary>Health probes run every few seconds and say nothing about users; keep them out of the traces.</summary>
    public static bool IsTracedRequest(HttpContext context)
        => !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);

    public static TelemetryExporter AddApiObservability(this WebApplicationBuilder builder)
    {
        var exporter = SelectExporter(builder.Configuration);
        if (exporter == TelemetryExporter.None)
            return exporter;

        // Serilog forwards every event to the remaining providers (writeToProviders in Program.cs). Only the
        // OpenTelemetry provider may remain, or each line would also reach the default console provider.
        builder.Logging.ClearProviders();

        var version = typeof(ObservabilityExtensions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options => options.Filter = IsTracedRequest);

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName, serviceVersion: version));

        if (exporter == TelemetryExporter.AzureMonitor)
        {
            // The distro brings ASP.NET Core, HttpClient and SQL client tracing, the standard request metrics and
            // the log exporter; it reads the connection string itself.
            telemetry.UseAzureMonitor();
            telemetry
                .WithTracing(tracing => tracing.AddSource(CrownusMetrics.ActivitySourceName))
                .WithMetrics(metrics => metrics.AddMeter(CrownusMetrics.MeterName).AddRuntimeInstrumentation());
            return exporter;
        }

        telemetry
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation()
                .AddSource(CrownusMetrics.ActivitySourceName)
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(CrownusMetrics.MeterName)
                .AddOtlpExporter())
            .WithLogging(logging => logging.AddOtlpExporter());
        return exporter;
    }
}
