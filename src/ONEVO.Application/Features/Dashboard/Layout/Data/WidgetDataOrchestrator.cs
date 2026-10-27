using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.DTOs;

namespace ONEVO.Application.Features.Dashboard.Layout.Data;

/// <summary>
/// Runs widget data providers with a concurrency cap and a per-provider deadline. A provider failure
/// (failed Result, exception or timeout) becomes that widget's <c>error</c> state, never a request failure.
/// <para>
/// Each provider runs in its own DI scope: providers send MediatR queries whose handlers use the scoped
/// ApplicationDbContext, which is not thread-safe, so concurrent providers must not share the request's
/// scope. The child scope sees the same caller because ICurrentUser reads IHttpContextAccessor (AsyncLocal,
/// flows into the provider tasks); the tenant context is a plain scoped object filled by middleware, so it
/// is copied from the request scope into each child scope before the provider (and its DbContext, whose
/// RLS interceptor reads it on connection open) is resolved.
/// </para>
/// </summary>
public sealed class WidgetDataOrchestrator(
    IServiceScopeFactory scopes,
    IServiceProviderIsKeyedService keyedServices,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<WidgetDataOrchestrator> logger) : IWidgetDataOrchestrator
{
    public const int MaxConcurrency = 4;
    public static readonly TimeSpan DefaultProviderTimeout = TimeSpan.FromSeconds(3);

    public TimeSpan ProviderTimeout { get; init; } = DefaultProviderTimeout;

    public async Task<IReadOnlyDictionary<string, (WidgetDataStateDto State, object? Data)>> LoadAsync(
        IReadOnlyList<string> widgetKeys, CancellationToken ct)
    {
        var keys = widgetKeys.Distinct(StringComparer.Ordinal).ToList();
        using var gate = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);

        var outcomes = await Task.WhenAll(keys.Select(key => RunAsync(key, gate, ct)));

        var result = new Dictionary<string, (WidgetDataStateDto State, object? Data)>(StringComparer.Ordinal);
        for (var i = 0; i < keys.Count; i++)
            result[keys[i]] = (outcomes[i].State, outcomes[i].Data);
        return result;
    }

    public async Task<WidgetDataOutcome> LoadOneAsync(string widgetKey, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(1, 1);
        return await RunAsync(widgetKey, gate, ct);
    }

    private async Task<WidgetDataOutcome> RunAsync(string key, SemaphoreSlim gate, CancellationToken ct)
    {
        if (!keyedServices.IsKeyedService(typeof(IWidgetDataProvider), key))
            return new WidgetDataOutcome(new WidgetDataStateDto(WidgetDataStatus.Client, null, null), null, null);

        await gate.WaitAsync(ct);
        try
        {
            return await RunProviderAsync(key, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<WidgetDataOutcome> RunProviderAsync(string key, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(ProviderTimeout);

        var scope = scopes.CreateAsyncScope();
        Task<Result<object>>? work = null;
        try
        {
            CopyTenantContext(scope.ServiceProvider);
            var provider = scope.ServiceProvider.GetRequiredKeyedService<IWidgetDataProvider>(key);
            work = provider.GetAsync(deadline.Token);

            // WaitAsync also cuts off a provider that ignores its token.
            var result = await work.WaitAsync(deadline.Token);
            return result.IsSuccess
                ? new WidgetDataOutcome(new WidgetDataStateDto(WidgetDataStatus.Success, clock.UtcNow, null), result.Value, null)
                : Failure(result.StatusCode ?? 400);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            logger.LogWarning("Dashboard widget provider {WidgetKey} exceeded its {Timeout} deadline.", key, ProviderTimeout);
            return new WidgetDataOutcome(
                new WidgetDataStateDto(WidgetDataStatus.Error, clock.UtcNow, WidgetDataErrors.Timeout), null, 504);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Dashboard widget provider {WidgetKey} failed.", key);
            return new WidgetDataOutcome(
                new WidgetDataStateDto(WidgetDataStatus.Error, clock.UtcNow, WidgetDataErrors.Failed), null, 500);
        }
        finally
        {
            await DisposeWhenDoneAsync(scope, work);
        }
    }

    private WidgetDataOutcome Failure(int statusCode)
    {
        var code = statusCode switch
        {
            403 => WidgetDataErrors.Forbidden,
            404 => WidgetDataErrors.NotFound,
            _ => WidgetDataErrors.Failed,
        };
        return new WidgetDataOutcome(new WidgetDataStateDto(WidgetDataStatus.Error, clock.UtcNow, code), null, statusCode);
    }

    /// <summary>
    /// Disposes the provider's scope now, or - when the provider was abandoned at its deadline and is still
    /// running - once it finishes, so its DbContext is never disposed underneath it.
    /// </summary>
    private static async Task DisposeWhenDoneAsync(AsyncServiceScope scope, Task<Result<object>>? work)
    {
        if (work is null || work.IsCompleted)
        {
            await scope.DisposeAsync();
            return;
        }

        _ = DisposeAfterAsync(scope, work);
    }

    private static async Task DisposeAfterAsync(AsyncServiceScope scope, Task work)
    {
        try
        {
            await work.ConfigureAwait(false);
        }
        catch
        {
            // The abandoned provider's outcome is no longer relevant; awaiting only observes it.
        }

        try
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Nothing left to report to: the request has already been answered.
        }
    }

    private void CopyTenantContext(IServiceProvider services)
    {
        var child = services.GetRequiredService<IWritableTenantContext>();
        switch (tenantContext.ContextMode)
        {
            case TenantContextMode.Tenant:
                child.Resolve(new TenantRegistryEntry(
                    tenantContext.TenantId, tenantContext.Slug ?? string.Empty,
                    tenantContext.Status ?? default, null));
                break;
            case TenantContextMode.Admin:
                child.SetAdminMode();
                break;
            default:
                child.SetSystemMode();
                break;
        }
    }
}
