using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Data;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Layout;

public sealed class WidgetDataOrchestratorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero) };
    private readonly ServiceCollection _services = new();

    public WidgetDataOrchestratorTests()
    {
        _services.AddScoped<TestTenantContext>();
        _services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TestTenantContext>());
        _services.AddScoped<IWritableTenantContext>(sp => sp.GetRequiredService<TestTenantContext>());
        _services.AddScoped<ScopeMarker>();
    }

    private void Provider(string key, Func<IServiceProvider, CancellationToken, Task<Result<object>>> load) =>
        _services.AddKeyedScoped<IWidgetDataProvider>(key, (sp, _) => new DelegateProvider(key, ct => load(sp, ct)));

    private WidgetDataOrchestrator Build(TimeSpan? timeout = null)
    {
        var root = _services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var requestTenant = new TestTenantContext();
        requestTenant.Resolve(new TenantRegistryEntry(TenantId, "acme", TenantStatus.Active, null));
        return new WidgetDataOrchestrator(
            root.GetRequiredService<IServiceScopeFactory>(),
            root.GetRequiredService<IServiceProviderIsKeyedService>(),
            requestTenant, _clock, NullLogger<WidgetDataOrchestrator>.Instance)
        {
            ProviderTimeout = timeout ?? WidgetDataOrchestrator.DefaultProviderTimeout,
        };
    }

    [Fact]
    public async Task Provider_exception_becomes_widget_error_and_others_succeed()
    {
        var payload = new { Hello = "world" };
        Provider("boom", (_, _) => throw new InvalidOperationException("secret connection string"));
        Provider("fine", (_, _) => Task.FromResult(Result<object>.Success(payload)));

        var data = await Build().LoadAsync(["boom", "fine"], default);

        Assert.Equal(WidgetDataStatus.Error, data["boom"].State.Status);
        Assert.Equal(WidgetDataErrors.Failed, data["boom"].State.Error);
        Assert.DoesNotContain("secret", data["boom"].State.Error);
        Assert.Null(data["boom"].Data);
        Assert.Equal(_clock.UtcNow, data["boom"].State.FetchedAt);

        Assert.Equal(WidgetDataStatus.Success, data["fine"].State.Status);
        Assert.Null(data["fine"].State.Error);
        Assert.Equal(_clock.UtcNow, data["fine"].State.FetchedAt);
        Assert.Same(payload, data["fine"].Data);
    }

    [Fact]
    public async Task Async_provider_exception_becomes_widget_error()
    {
        Provider("boom", async (_, ct) =>
        {
            await Task.Yield();
            throw new InvalidOperationException("SELECT * FROM secrets");
        });

        var outcome = await Build().LoadOneAsync("boom", default);

        Assert.Equal(WidgetDataStatus.Error, outcome.State.Status);
        Assert.Equal(WidgetDataErrors.Failed, outcome.State.Error);
        Assert.Equal(500, outcome.FailureStatusCode);
    }

    [Fact]
    public async Task Slow_provider_times_out_into_error()
    {
        // One provider honours the token, one ignores it: both must be cut off at the deadline.
        Provider("cooperative", async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Result<object>.Success(1);
        });
        Provider("stubborn", async (_, _) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            return Result<object>.Success(2);
        });
        Provider("fast", (_, _) => Task.FromResult(Result<object>.Success(3)));

        var watch = Stopwatch.StartNew();
        var data = await Build(TimeSpan.FromMilliseconds(200)).LoadAsync(["cooperative", "stubborn", "fast"], default);
        watch.Stop();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
        Assert.Equal(WidgetDataStatus.Error, data["cooperative"].State.Status);
        Assert.Equal(WidgetDataErrors.Timeout, data["cooperative"].State.Error);
        Assert.Equal(WidgetDataStatus.Error, data["stubborn"].State.Status);
        Assert.Equal(WidgetDataErrors.Timeout, data["stubborn"].State.Error);
        Assert.Equal(WidgetDataStatus.Success, data["fast"].State.Status);
    }

    [Fact]
    public async Task Timeout_reports_gateway_timeout_status()
    {
        Provider("slow", async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Result<object>.Success(1);
        });

        var outcome = await Build(TimeSpan.FromMilliseconds(100)).LoadOneAsync("slow", default);

        Assert.Equal(WidgetDataErrors.Timeout, outcome.State.Error);
        Assert.Equal(504, outcome.FailureStatusCode);
    }

    [Fact]
    public async Task Widget_without_provider_is_client()
    {
        var data = await Build().LoadAsync(["todays-schedule"], default);

        var (state, payload) = data["todays-schedule"];
        Assert.Equal(WidgetDataStatus.Client, state.Status);
        Assert.Null(state.FetchedAt);
        Assert.Null(state.Error);
        Assert.Null(payload);
    }

    [Fact]
    public async Task Never_more_than_4_concurrent()
    {
        var current = 0;
        var peak = 0;
        var keys = Enumerable.Range(0, 10).Select(i => $"w{i}").ToList();
        foreach (var key in keys)
        {
            Provider(key, async (_, ct) =>
            {
                var now = Interlocked.Increment(ref current);
                int seen;
                while ((seen = Volatile.Read(ref peak)) < now && Interlocked.CompareExchange(ref peak, now, seen) != seen)
                {
                }
                await Task.Delay(60, ct);
                Interlocked.Decrement(ref current);
                return Result<object>.Success(key);
            });
        }

        var data = await Build().LoadAsync(keys, default);

        Assert.Equal(WidgetDataOrchestrator.MaxConcurrency, peak);
        Assert.All(keys, k => Assert.Equal(WidgetDataStatus.Success, data[k].State.Status));
        Assert.All(keys, k => Assert.Equal(k, data[k].Data));
    }

    [Fact]
    public async Task Failed_result_becomes_error_and_keeps_status_code()
    {
        Provider("denied", (_, _) => Task.FromResult(Result<object>.Forbidden("Permission 'x' required.")));
        Provider("missing", (_, _) => Task.FromResult(Result<object>.NotFound("Current employee record was not found.")));
        Provider("bad", (_, _) => Task.FromResult(Result<object>.Failure("nope", 400)));

        var orchestrator = Build();
        var denied = await orchestrator.LoadOneAsync("denied", default);
        var missing = await orchestrator.LoadOneAsync("missing", default);
        var bad = await orchestrator.LoadOneAsync("bad", default);

        Assert.Equal((WidgetDataStatus.Error, WidgetDataErrors.Forbidden, 403), (denied.State.Status, denied.State.Error, denied.FailureStatusCode));
        Assert.Equal((WidgetDataStatus.Error, WidgetDataErrors.NotFound, 404), (missing.State.Status, missing.State.Error, missing.FailureStatusCode));
        Assert.Equal((WidgetDataStatus.Error, WidgetDataErrors.Failed, 400), (bad.State.Status, bad.State.Error, bad.FailureStatusCode));
        Assert.Null(denied.Data);
    }

    [Fact]
    public async Task Each_provider_runs_in_its_own_scope_with_the_request_tenant()
    {
        var seen = new System.Collections.Concurrent.ConcurrentBag<(ScopeMarker Marker, ITenantContext Tenant)>();
        foreach (var key in new[] { "a", "b", "c" })
        {
            Provider(key, (sp, _) =>
            {
                seen.Add((sp.GetRequiredService<ScopeMarker>(), sp.GetRequiredService<ITenantContext>()));
                return Task.FromResult(Result<object>.Success(key));
            });
        }

        await Build().LoadAsync(["a", "b", "c"], default);

        Assert.Equal(3, seen.Count);
        Assert.Equal(3, seen.Select(s => s.Marker).Distinct().Count());
        Assert.All(seen, s =>
        {
            Assert.Equal(TenantContextMode.Tenant, s.Tenant.ContextMode);
            Assert.Equal(TenantId, s.Tenant.TenantId);
            Assert.Equal("acme", s.Tenant.Slug);
        });
    }

    [Fact]
    public async Task Duplicate_keys_load_once()
    {
        var calls = 0;
        Provider("a", (_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(Result<object>.Success(1));
        });

        var data = await Build().LoadAsync(["a", "a"], default);

        Assert.Single(data);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        Provider("slow", async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Result<object>.Success(1);
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Build().LoadAsync(["slow"], cts.Token));
    }

    private sealed class ScopeMarker;

    private sealed class DelegateProvider(string key, Func<CancellationToken, Task<Result<object>>> load) : IWidgetDataProvider
    {
        public string WidgetKey => key;

        public Task<Result<object>> GetAsync(CancellationToken ct) => load(ct);
    }

    private sealed class TestTenantContext : IWritableTenantContext
    {
        public Guid TenantId { get; private set; }
        public string? Slug { get; private set; }
        public TenantStatus? Status { get; private set; }
        public bool IsResolved { get; private set; }
        public TenantContextMode ContextMode { get; private set; } = TenantContextMode.System;

        public void Resolve(TenantRegistryEntry tenant)
        {
            TenantId = tenant.TenantId;
            Slug = tenant.Slug;
            Status = tenant.Status;
            IsResolved = true;
            ContextMode = TenantContextMode.Tenant;
        }

        public void SetAdminMode() => ContextMode = TenantContextMode.Admin;

        public void SetSystemMode() => ContextMode = TenantContextMode.System;
    }
}
