using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.Monitoring.Retention;

/// <summary>Runs <see cref="MonitoringRetentionPurgeRunner"/> on a fixed interval (MON-02).</summary>
public sealed class MonitoringRetentionPurgeJob : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly IServiceProvider _services;
    private readonly ILogger<MonitoringRetentionPurgeJob> _logger;

    public MonitoringRetentionPurgeJob(IServiceProvider services, ILogger<MonitoringRetentionPurgeJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);

        try
        {
            do
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MonitoringRetentionPurgeJob encountered an error.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // PeriodicTimer.WaitForNextTickAsync throws when the token passed to it is
            // cancelled (unlike disposing the timer itself, which returns false instead) -
            // an ordinary host shutdown must not surface as an unhandled exception here,
            // since HostOptions.BackgroundServiceExceptionBehavior = StopHost treats any
            // unhandled exception from a BackgroundService as a crash.
        }
    }

    /// <summary>Public entry for tests / manual triggers - same precedent as ActivityDailySummaryJob.RunAggregationAsync.</summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMonitoringRetentionPurgeRunner>();
        await runner.RunOnceAsync(ct);
    }
}
