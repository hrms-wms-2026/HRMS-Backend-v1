using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.SharedPlatform.Outbox;

/// <summary>
/// Task 31: refuses to start when two outbox handlers claim the same message type, instead of failing every outbox
/// batch later. Runs on every instance, including web-only ones (BackgroundJobs:Enabled=false), because those write
/// outbox messages too.
/// </summary>
public sealed class OutboxHandlerStartupCheck : IHostedService
{
    private readonly IServiceScopeFactory _scopes;

    public OutboxHandlerStartupCheck(IServiceScopeFactory scopes) => _scopes = scopes;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        OutboxHandlerMap.Build(scope.ServiceProvider.GetServices<IOutboxMessageHandler>());
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
