using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Services.SharedPlatform.Jobs;

namespace ONEVO.Infrastructure.Services.SharedPlatform.Outbox;

/// <summary>
/// Task 31: deletes published outbox messages older than <c>Outbox:PublishedRetentionDays</c> (default 14), in
/// batches, so the table does not grow without bound. Failed messages are kept: they are the evidence for the
/// outbox failure alert and are cleared by hand once dealt with. Pending and processing messages are never touched.
/// </summary>
public sealed class OutboxRetentionPurgeJob : BackgroundService
{
    public const string RetentionDaysKey = "Outbox:PublishedRetentionDays";
    public const int DefaultRetentionDays = 14;
    public const int BatchSize = 1000;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    public static readonly JobSchedule Schedule = JobSchedule.Every("outbox-retention-purge", CheckInterval, TimeSpan.FromMinutes(30));

    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OutboxRetentionPurgeJob> _logger;

    public OutboxRetentionPurgeJob(IServiceProvider services, IConfiguration configuration, ILogger<OutboxRetentionPurgeJob> logger)
    {
        _services = services;
        _configuration = configuration;
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
                    await _services.RunExclusiveAsync(Schedule, async ct => (int?)await RunOnceAsync(ct), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OutboxRetentionPurgeJob encountered an error.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown while waiting for the next tick (see MonitoringRetentionPurgeJob).
        }
    }

    /// <summary>One purge pass. Returns the number of rows deleted. Public for tests and manual runs.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var days = _configuration.GetValue(RetentionDaysKey, DefaultRetentionDays);
        if (days < 1)
            days = DefaultRetentionDays;

        await using var scope = _services.CreateAsyncScope();
        // Outbox rows of every tenant (and tenant-less platform rows) are purged together.
        scope.ServiceProvider.GetRequiredService<IWritableTenantContext>().SetSystemMode();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);

        var total = 0;
        while (!ct.IsCancellationRequested)
        {
            var deleted = await db.Set<OutboxMessage>()
                .Where(m => m.Status == OutboxMessageStatus.Published && m.ProcessedAt != null && m.ProcessedAt < cutoff)
                .OrderBy(m => m.ProcessedAt)
                .Take(BatchSize)
                .ExecuteDeleteAsync(ct);
            total += deleted;
            if (deleted < BatchSize)
                break;
        }

        if (total > 0)
            _logger.LogInformation("Purged {Count} published outbox messages older than {Days} days.", total, days);
        return total;
    }
}
