using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.Auth.Login;

public sealed class LoginWorkspaceSelectionChallengeCleanupService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

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
                await using var scope = _services.CreateAsyncScope();
                var runner = scope.ServiceProvider.GetRequiredService<ILoginWorkspaceSelectionChallengeCleanupRunner>();
                var deletedRows = await runner.RunOnceAsync(stoppingToken);

                if (deletedRows > 0)
                {
                    _logger.LogInformation(
                        "Deleted {Count} expired/consumed login_workspace_selection_challenges rows older than 24 hours.",
                        deletedRows);
                }
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
