using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Services.SharedPlatform.Notifications;

/// <summary>
/// Marks tray deliveries nobody collected before their expiry as Expired, so a device that is offline or
/// not installed shows an honest status instead of "pending" forever. One admin-mode update across tenants.
/// </summary>
public sealed class NotificationTrayExpiryJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private readonly IServiceProvider _services;
    private readonly ILogger<NotificationTrayExpiryJob> _logger;

    public NotificationTrayExpiryJob(IServiceProvider services, ILogger<NotificationTrayExpiryJob> logger)
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
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tray notification expiry sweep failed; will retry.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IWritableTenantContext>().SetAdminMode();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var expired = await scope.ServiceProvider.GetRequiredService<INotificationDeliveryRepository>()
            .ExpirePendingTrayAsync(clock.UtcNow, ct);
        if (expired > 0)
            _logger.LogInformation("Expired {Count} uncollected tray notification deliveries.", expired);
    }
}
