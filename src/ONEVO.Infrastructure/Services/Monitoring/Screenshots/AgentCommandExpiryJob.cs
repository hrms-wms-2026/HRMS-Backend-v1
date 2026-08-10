using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Services.Monitoring.Screenshots;

public sealed class AgentCommandExpiryJob : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(2);

    private readonly IServiceProvider _services;
    private readonly ILogger<AgentCommandExpiryJob> _logger;

    public AgentCommandExpiryJob(IServiceProvider services, ILogger<AgentCommandExpiryJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _services.CreateAsyncScope();
                var repo = scope.ServiceProvider.GetRequiredService<IAgentCommandRepository>();
                var expired = await repo.ExpireStaleCommandsAsync(DateTimeOffset.UtcNow, stoppingToken);

                if (expired > 0)
                    _logger.LogInformation("Expired {Count} stale agent commands.", expired);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AgentCommandExpiryJob encountered an error.");
            }
        }
    }
}
