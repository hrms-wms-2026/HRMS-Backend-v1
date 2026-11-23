using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Api.Extensions;
using ONEVO.Application.Common.Observability;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Infrastructure.Observability;
using ONEVO.Infrastructure.Services.SharedPlatform.Jobs;
using Xunit;

namespace ONEVO.Tests.Unit.Infrastructure.Observability;

public class ObservabilityTests
{
    // ---- exporter choice ------------------------------------------------------------------------

    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void Exporter_NoneByDefault()
        => ObservabilityExtensions.SelectExporter(Config()).Should().Be(ObservabilityExtensions.TelemetryExporter.None);

    [Fact]
    public void Exporter_Otlp_WhenOnlyTheEndpointIsSet()
        => ObservabilityExtensions.SelectExporter(Config((ObservabilityExtensions.OtlpEndpointKey, "https://otlp.example.net")))
            .Should().Be(ObservabilityExtensions.TelemetryExporter.Otlp);

    [Fact]
    public void Exporter_AzureMonitorWins_WhenBothAreSet()
        => ObservabilityExtensions.SelectExporter(Config(
                (ObservabilityExtensions.OtlpEndpointKey, "https://otlp.example.net"),
                (ObservabilityExtensions.AzureMonitorConnectionKey, "InstrumentationKey=00000000-0000-0000-0000-000000000000")))
            .Should().Be(ObservabilityExtensions.TelemetryExporter.AzureMonitor);

    [Theory]
    [InlineData("/health", false)]
    [InlineData("/health/ready", false)]
    [InlineData("/api/v1/inbox", true)]
    [InlineData("/healthz", true)]
    public void HealthProbes_AreNotTraced(string path, bool traced)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        ObservabilityExtensions.IsTracedRequest(context).Should().Be(traced);
    }

    // ---- redaction ------------------------------------------------------------------------------

    [Theory]
    [InlineData("jane.doe@acme.com", "j***@acme.com")]
    [InlineData("a@b.io", "a***@b.io")]
    [InlineData("not-an-address", "***")]
    [InlineData("@acme.com", "***")]
    [InlineData("jane@", "***")]
    [InlineData("", "***")]
    [InlineData(null, "***")]
    public void MaskEmail_KeepsOnlyTheFirstLetterAndTheDomain(string? email, string expected)
        => LogRedaction.MaskEmail(email).Should().Be(expected);

    // ---- metrics --------------------------------------------------------------------------------

    private sealed class Recorded
    {
        public readonly List<(string Instrument, double Value, Dictionary<string, object?> Tags)> Items = [];
    }

    private static MeterListener Listen(Recorded recorded, params string[] instruments)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == CrownusMetrics.MeterName && instruments.Contains(instrument.Name))
                    l.EnableMeasurementEvents(instrument);
            },
        };
        void Add(Instrument i, double v, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var copy = new Dictionary<string, object?>();
            foreach (var tag in tags)
                copy[tag.Key] = tag.Value;
            lock (recorded)
                recorded.Items.Add((i.Name, v, copy));
        }
        listener.SetMeasurementEventCallback<long>((i, v, tags, _) => Add(i, v, tags));
        listener.SetMeasurementEventCallback<double>((i, v, tags, _) => Add(i, v, tags));
        listener.Start();
        return listener;
    }

    private static ExclusiveJobRunner Runner(bool leaseFree)
    {
        var leases = new Mock<IJobLeaseService>();
        leases.Setup(l => l.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(leaseFree);
        leases.Setup(l => l.RenewAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return new ExclusiveJobRunner(leases.Object, NullLogger<ExclusiveJobRunner>.Instance);
    }

    [Fact]
    public async Task JobRuns_AreCountedByOutcome_AndTimedWhenTheyDidWork()
    {
        var recorded = new Recorded();
        using var _ = Listen(recorded, "crownus.jobs.runs", "crownus.jobs.duration");
        var job = $"metrics-test-{Guid.NewGuid():N}";
        var schedule = new JobSchedule(job, TimeSpan.FromMinutes(1), TimeSpan.Zero);

        await Runner(leaseFree: true).RunAsync(schedule, _ => Task.CompletedTask, CancellationToken.None);
        await Runner(leaseFree: false).RunAsync(schedule, _ => Task.CompletedTask, CancellationToken.None);
        await Runner(leaseFree: true).Invoking(r => r.RunAsync(schedule, _ => throw new InvalidOperationException(), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        var mine = recorded.Items.Where(i => Equals(i.Tags["job"], job)).ToList();
        mine.Where(i => i.Instrument == "crownus.jobs.runs").Select(i => i.Tags["outcome"])
            .Should().BeEquivalentTo(["completed", "skipped", "failed"]);
        mine.Where(i => i.Instrument == "crownus.jobs.duration").Select(i => i.Tags["outcome"])
            .Should().BeEquivalentTo(["completed", "failed"]);
    }

    [Fact]
    public void SignInFailures_AndRateLimitRejections_CarryOnlyNonPersonalTags()
    {
        var recorded = new Recorded();
        using var _ = Listen(recorded, "crownus.auth.signin_failures", "crownus.ratelimit.rejections");

        CrownusMetrics.RecordSignInFailure("tenant", 401);
        CrownusMetrics.RecordRateLimitRejection("auth", "ip");

        recorded.Items.Should().Contain(i => i.Instrument == "crownus.auth.signin_failures"
            && Equals(i.Tags["surface"], "tenant") && Equals(i.Tags["status"], 401) && i.Tags.Count == 2);
        recorded.Items.Should().Contain(i => i.Instrument == "crownus.ratelimit.rejections"
            && Equals(i.Tags["limiter"], "auth") && Equals(i.Tags["partition"], "ip") && i.Tags.Count == 2);
    }

    // ---- readiness ------------------------------------------------------------------------------

    private sealed class FixedBacklog(OutboxBacklog? backlog, Exception? error = null) : IOutboxBacklogSource
    {
        public Task<OutboxBacklog> GetAsync(CancellationToken ct)
            => error is null ? Task.FromResult(backlog!) : Task.FromException<OutboxBacklog>(error);
    }

    private static Task<HealthCheckResult> Outbox(OutboxBacklog? backlog, Exception? error = null)
        => new OutboxBacklogHealthCheck(new FixedBacklog(backlog, error)).CheckHealthAsync(new HealthCheckContext());

    [Fact]
    public async Task Outbox_Moving_IsHealthy_WithCounts()
    {
        var result = await Outbox(new OutboxBacklog(3, 1, TimeSpan.FromMinutes(2), DateTimeOffset.UtcNow));

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["pending"].Should().Be(3);
        result.Data["failed"].Should().Be(1);
    }

    [Fact]
    public async Task Outbox_Empty_IsHealthy()
        => (await Outbox(new OutboxBacklog(0, 0, null, DateTimeOffset.UtcNow))).Status.Should().Be(HealthStatus.Healthy);

    [Fact]
    public async Task Outbox_Stuck_IsDegraded_NotUnhealthy()
        => (await Outbox(new OutboxBacklog(40, 0, TimeSpan.FromMinutes(16), DateTimeOffset.UtcNow))).Status
            .Should().Be(HealthStatus.Degraded);

    [Fact]
    public async Task Outbox_Unreadable_IsDegraded()
        => (await Outbox(null, new InvalidOperationException("db"))).Status.Should().Be(HealthStatus.Degraded);

    private static Task<HealthCheckResult> Storage(Func<CancellationToken, Task<bool>> exists)
    {
        var adapter = new Mock<IObjectStorageAdapter>();
        adapter.Setup(a => a.ObjectExistsAsync(ObjectStorageHealthCheck.ProbeKey, It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) => exists(ct));
        var services = new ServiceCollection()
            .AddScoped(_ => adapter.Object)
            .AddScoped(_ => Mock.Of<IWritableTenantContext>())
            .BuildServiceProvider();
        return new ObjectStorageHealthCheck(services.GetRequiredService<IServiceScopeFactory>())
            .CheckHealthAsync(new HealthCheckContext());
    }

    [Fact]
    public async Task Storage_Answers_IsHealthy_WhateverTheAnswer()
    {
        (await Storage(_ => Task.FromResult(false))).Status.Should().Be(HealthStatus.Healthy);
        (await Storage(_ => Task.FromResult(true))).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Storage_Fails_IsDegraded()
        => (await Storage(_ => throw new HttpRequestException("403"))).Status.Should().Be(HealthStatus.Degraded);

    [Fact]
    public async Task Storage_Hangs_IsDegraded_AfterTheTimeout()
        => (await Storage(async ct => { await Task.Delay(Timeout.Infinite, ct); return true; })).Status
            .Should().Be(HealthStatus.Degraded);
}
