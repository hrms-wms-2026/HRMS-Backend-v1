using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ONEVO.Infrastructure.Services.SharedPlatform.RateLimiting;

/// <summary>
/// Task 46: loads the rate limit overrides before the API takes traffic, then checks every
/// 30 seconds whether they, a tenant's plan or appsettings changed, and reloads when one did. This
/// is how other API instances pick up an admin change until task 21 adds a shared signal.
/// The first load waits at most <see cref="StartupTimeout"/>; if it fails, the API starts on
/// appsettings limits and the next check tries again.
/// </summary>
public sealed class RateLimitSettingsRefreshService(
    DatabaseRateLimitSettingsProvider provider,
    IRateLimitOverrideLoader loader,
    ILogger<RateLimitSettingsRefreshService> logger) : IHostedService, IDisposable
{
    internal static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource _stopping = new();
    private Task? _loop;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StartupTimeout);
        try
        {
            await provider.ReloadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Loading rate limit overrides took longer than {Timeout}; starting on appsettings limits",
                StartupTimeout);
        }

        _loop = Task.Run(() => LoopAsync(_stopping.Token), CancellationToken.None);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        if (_loop is not null)
            await Task.WhenAny(_loop, Task.Delay(Timeout.Infinite, cancellationToken));
    }

    /// <summary>One 30-second check: rebuild on an appsettings change, reload on a data change.</summary>
    public async Task CheckOnceAsync(CancellationToken ct)
    {
        if (provider.BaseChangedSinceBuild)
            await provider.RebuildFromLastDataAsync();

        var stamp = await loader.GetStampAsync(ct);
        if (stamp != provider.LastStamp)
            await provider.ReloadAsync(ct);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    await CheckOnceAsync(ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogError(ex, "Checking rate limit overrides for changes failed");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    public void Dispose() => _stopping.Dispose();
}
