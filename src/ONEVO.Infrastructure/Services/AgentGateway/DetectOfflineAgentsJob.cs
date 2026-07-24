using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
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
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var threshold = DateTimeOffset.UtcNow.Subtract(OfflineThreshold);
        var agentIds = await repo.MarkAgentsInactiveAndReturnIdsAsync(threshold, ct);

        if (agentIds.Count == 0) return;

        foreach (var agentId in agentIds)
        {
            await outbox.EnqueueAsync("AgentHeartbeatLost", new
            {
                agent_id = agentId,
                detected_at = DateTimeOffset.UtcNow,
                offline_threshold_minutes = (int)OfflineThreshold.TotalMinutes
            }, ct: ct);
        }

        await uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Marked {Count} agent(s) inactive and wrote AgentHeartbeatLost outbox events (threshold: {Threshold}).",
            agentIds.Count, threshold);
    }
}
