using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using Xunit;

namespace ONEVO.Tests.Integration.SharedPlatform;

/// <summary>
/// Several OutboxProcessor instances (as when the API is scaled out) draining the same outbox
/// table. Delivery guarantee under test: at-least-once with no concurrent double-processing - never
/// exactly-once. With no crash, each message is therefore dispatched exactly once.
/// Requires Docker.
/// </summary>
public sealed class OutboxProcessorConcurrencyIntegrationTests
{
    private const string ConcurrencyType = "outbox_it_concurrency";

    /// <summary>Records every dispatch and flags any message being handled by two processors at
    /// the same moment.</summary>
    private sealed class DispatchLog
    {
        public ConcurrentDictionary<Guid, int> InFlight { get; } = new();
        public ConcurrentDictionary<Guid, int> Dispatches { get; } = new();
        public ConcurrentBag<Guid> Overlaps { get; } = new();
    }

    private sealed class SlowRecordingHandler(DispatchLog log) : IOutboxMessageHandler
    {
        public string Type => ConcurrencyType;

        public async Task HandleAsync(string payloadJson, CancellationToken ct)
        {
            var id = JsonSerializer.Deserialize<TestPayload>(payloadJson)!.MessageId;
            if (log.InFlight.AddOrUpdate(id, 1, (_, n) => n + 1) > 1)
                log.Overlaps.Add(id);
            try
            {
                log.Dispatches.AddOrUpdate(id, 1, (_, n) => n + 1);
                // Widen the window in which a competing processor could pick the same row.
                await Task.Delay(Random.Shared.Next(5, 25), ct);
            }
            finally
            {
                log.InFlight.AddOrUpdate(id, 0, (_, n) => n - 1);
            }
        }
    }

    [Fact]
    public async Task FourProcessors_DrainingTheSamePendingSet_DispatchEachMessageExactlyOnce_NeverConcurrently()
    {
        var host = await OutboxTestHost.CreateAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 60; i++)
            ids.Add(await host.SeedPendingAsync(ConcurrencyType, tenantId: i % 2 == 0 ? host.TenantId : null));

        var log = new DispatchLog();
        var processors = Enumerable.Range(0, 4)
            .Select(_ => host.CreateProcessor(s => s.AddScoped<IOutboxMessageHandler>(_ => new SlowRecordingHandler(log))))
            .ToList();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        // Start every processor at the same moment so they race for the same first batch.
        using var gate = new SemaphoreSlim(0);
        var runs = processors.Select(p => Task.Run(async () =>
        {
            await gate.WaitAsync(timeout.Token);
            return await OutboxTestHost.DrainAsync(p, timeout.Token);
        })).ToList();
        gate.Release(processors.Count);
        var claimedPerProcessor = await Task.WhenAll(runs);

        // The processors really competed for the set; claims never overlap, so they sum to it.
        Assert.True(claimedPerProcessor.Count(n => n > 0) >= 2, $"claims per processor: {string.Join(", ", claimedPerProcessor)}");
        Assert.Equal(ids.Count, claimedPerProcessor.Sum());
        Assert.Empty(log.Overlaps);
        Assert.Equal(ids.Count, log.Dispatches.Count);
        Assert.All(ids, id => Assert.Equal(1, log.Dispatches.GetValueOrDefault(id)));

        var rows = await host.GetAllAsync();
        Assert.All(rows, row =>
        {
            Assert.Equal(OutboxMessageStatus.Published, row.Status);
            Assert.Equal(0, row.AttemptCount);
            Assert.NotNull(row.ProcessedAt);
        });
    }
}
