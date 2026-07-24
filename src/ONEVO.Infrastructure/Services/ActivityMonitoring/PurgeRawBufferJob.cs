using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Services.ActivityMonitoring;

public sealed class PurgeRawBufferJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromHours(48);

    private readonly IServiceProvider _services;
    private readonly ILogger<PurgeRawBufferJob> _logger;

    public PurgeRawBufferJob(IServiceProvider services, ILogger<PurgeRawBufferJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PurgeRawBufferJob failed; will retry next interval.");
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IActivityMonitoringRepository>();

        var cutoff = DateTimeOffset.UtcNow.Subtract(RetentionWindow);
        var deleted = await repo.DeleteRawBufferOlderThanAsync(cutoff, ct);

        if (deleted > 0)
            _logger.LogInformation("PurgeRawBufferJob: deleted {Count} raw buffer rows older than {Cutoff}.",
                deleted, cutoff);
    }
}
