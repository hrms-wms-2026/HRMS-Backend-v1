using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Services.SharedPlatform.Jobs;

namespace ONEVO.Infrastructure.Services.Auth.Login;

public sealed class LoginWorkspaceSelectionChallengeCleanupService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);
    public static readonly JobSchedule Schedule = JobSchedule.Every(
        "login-workspace-challenge-cleanup", PollInterval, TimeSpan.FromMinutes(10));

    private readonly IServiceProvider _services;
    private readonly ILogger<LoginWorkspaceSelectionChallengeCleanupService> _logger;

    public LoginWorkspaceSelectionChallengeCleanupService(
        IServiceProvider services,
        ILogger<LoginWorkspaceSelectionChallengeCleanupService> logger)
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
                // The runner's log line carries the deleted row count.
                await _services.RunExclusiveAsync(Schedule, async ct =>
                {
                    await using var scope = _services.CreateAsyncScope();
                    var runner = scope.ServiceProvider.GetRequiredService<ILoginWorkspaceSelectionChallengeCleanupRunner>();
                    return (int?)await runner.RunOnceAsync(ct);
                }, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Login workspace selection challenge cleanup iteration failed; will retry next tick.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
