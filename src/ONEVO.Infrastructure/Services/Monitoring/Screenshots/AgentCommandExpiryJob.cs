using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
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
                await RunOnceAsync(stoppingToken);
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

    /// <summary>Public entry for tests / manual triggers - same precedent as
    /// ActivityDailySummaryJob.RunAggregationAsync.</summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAgentCommandRepository>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();

        // agent_commands is under FORCE row-level security with an admin-or-matching-
        // tenant policy. This is a single cross-tenant bulk UPDATE (no per-tenant writes
        // to stage), so admin mode for the whole call is sufficient - a background scope
        // otherwise defaults to system mode, which the policy admits for neither USING
        // nor WITH CHECK, so the update would silently match zero rows.
        tenantContext.SetAdminMode();

        var expired = await repo.ExpireStaleCommandsAsync(DateTimeOffset.UtcNow, ct);

        if (expired > 0)
            _logger.LogInformation("Expired {Count} stale agent commands.", expired);
    }
}
