using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using ONEVO.Api.Middleware;
using ONEVO.Tests.Integration.Support;
using Xunit;

namespace ONEVO.Tests.Integration.Observability;

/// <summary>
/// Task 17 "a request can be followed from the browser correlation id to its SQL calls": boots the real API with an
/// OTLP endpoint set (so tracing is on; the endpoint is a discard address), captures every span in process, and
/// checks that a sign-in request's span carries the correlation id and that its SQL spans share its trace.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class TracingIntegrationTests : IAsyncLifetime
{
    private const string OtlpEndpoint = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private readonly ConcurrentBag<Activity> _stopped = [];
    private ActivityListener _listener = null!;
    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private ApiBootTestFactory _factory = null!;
    private string? _previousEndpoint;

    public async Task InitializeAsync()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _stopped.Add(activity),
        };
        ActivitySource.AddActivityListener(_listener);

        _previousEndpoint = Environment.GetEnvironmentVariable(OtlpEndpoint);
        Environment.SetEnvironmentVariable(OtlpEndpoint, "http://127.0.0.1:9");
        var connectionString = await SharedSqlServerTemplate.CreateDatabaseAsync();
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);
        _factory = new ApiBootTestFactory(connectionString);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _environmentScope.DisposeAsync();
        Environment.SetEnvironmentVariable(OtlpEndpoint, _previousEndpoint);
        _listener.Dispose();
    }

    [Fact]
    public async Task SignInRequest_CarriesTheCorrelationId_AndItsSqlCallsShareTheTrace()
    {
        var client = _factory.CreateClient();
        var correlationId = $"trace-test-{Guid.NewGuid():N}";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email = "nobody@example.invalid", password = "wrong-password-123" }),
        };
        request.Headers.Add("X-Correlation-Id", correlationId);

        var response = await client.SendAsync(request);
        Assert.False(response.IsSuccessStatusCode);

        Activity? server = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (server is null && DateTime.UtcNow < deadline)
        {
            server = _stopped.FirstOrDefault(a => a.Kind == ActivityKind.Server
                && Equals(a.GetTagItem(CorrelationIdMiddleware.CorrelationIdTag), correlationId));
            if (server is null)
                await Task.Delay(100);
        }

        Assert.NotNull(server);
        var sql = _stopped.Where(a => a.TraceId == server!.TraceId
            && (a.GetTagItem("db.system") is not null || a.GetTagItem("db.system.name") is not null)).ToList();
        Assert.NotEmpty(sql);
    }

    [Fact]
    public async Task HealthProbes_AreNotTraced()
    {
        var client = _factory.CreateClient();
        await client.GetAsync("/health");
        await client.GetAsync("/health/ready");
        await Task.Delay(200);

        Assert.DoesNotContain(_stopped, a => a.Kind == ActivityKind.Server
            && (a.GetTagItem("url.path") as string)?.StartsWith("/health", StringComparison.Ordinal) == true);
    }
}
