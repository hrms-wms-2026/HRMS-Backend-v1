using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Services.AgentGateway;

public sealed class DetectOfflineAgentsJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan OfflineThreshold = TimeSpan.FromMinutes(5);

    private readonly IServiceProvider _services;
    private readonly ILogger<DetectOfflineAgentsJob> _logger;

    public DetectOfflineAgentsJob(IServiceProvider services, ILogger<DetectOfflineAgentsJob> logger)
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
                _logger.LogWarning(ex, "DetectOfflineAgentsJob iteration failed; will retry next interval.");
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAgentGatewayRepository>();

        var threshold = DateTimeOffset.UtcNow.Subtract(OfflineThreshold);
        var affected = await repo.MarkAgentsInactiveAsync(threshold, ct);

        if (affected > 0)
            _logger.LogInformation("Marked {Count} agent(s) inactive (no heartbeat since {Threshold}).",
                affected, threshold);
    }
}
