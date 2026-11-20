using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Api.Extensions;
using ONEVO.Api.Filters;
using ONEVO.Api.Middleware;
using Xunit;

namespace ONEVO.Tests.Unit.Api.Performance;

/// <summary>Improvements task 10: compression, null skipping and Cache-Control on API responses.</summary>
public sealed class ResponseCompressionAndCachingTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(TestPerformanceController).Assembly)
            .AddJsonOptions(o => o.ConfigureApiJson());
        builder.Services.AddApiResponseCompression();

        _app = builder.Build();
        _app.UseResponseCompression();
        _app.UseMiddleware<ApiCacheControlMiddleware>();
        _app.MapControllers();
        _app.MapGet("/spa-file", () => Results.Text("{}", "application/json"));
        await _app.StartAsync();

        _client = _app.GetTestClient();
        _client.BaseAddress = new Uri("https://localhost");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private Task<HttpResponseMessage> GetAsync(string path, string? encoding = null, string? ifNoneMatch = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (encoding is not null) request.Headers.AcceptEncoding.ParseAdd(encoding);
        if (ifNoneMatch is not null) request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        return _client.SendAsync(request);
    }

    [Theory]
    [InlineData("br")]
    [InlineData("gzip")]
    public async Task Json_over_https_is_compressed(string encoding)
    {
        using var response = await GetAsync("/api/test-perf/large", encoding);

        response.Content.Headers.ContentEncoding.Should().Equal(encoding);
        var compressed = (await response.Content.ReadAsByteArrayAsync()).Length;
        compressed.Should().BeLessThan(TestPerformanceController.Large().Sum(r => r.Name.Length) / 2);
    }

    [Fact]
    public async Task Problem_json_is_compressed()
    {
        using var response = await GetAsync("/api/test-perf/problem", "br");

        response.Content.Headers.ContentEncoding.Should().Equal("br");
    }

    [Fact]
    public async Task A_response_marked_NoResponseCompression_is_not_compressed()
    {
        using var response = await GetAsync("/api/test-perf/secret", "br, gzip");

        response.Content.Headers.ContentEncoding.Should().BeEmpty();
        (await response.Content.ReadAsStringAsync()).Should().Contain("token-value");
    }

    [Fact]
    public async Task Null_properties_are_left_out()
    {
        using var response = await GetAsync("/api/test-perf/nulls");

        (await response.Content.ReadAsStringAsync()).Should().Be("{\"name\":\"a\"}");
    }

    [Fact]
    public async Task Api_responses_default_to_no_store()
    {
        using var response = await GetAsync("/api/test-perf/nulls");

        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task An_endpoint_that_sets_cache_control_keeps_it()
    {
        using var response = await GetAsync("/api/test-perf/own-cache");

        response.Headers.CacheControl!.Private.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromSeconds(900));
        response.Headers.CacheControl.NoStore.Should().BeFalse();
    }

    [Fact]
    public async Task Non_api_paths_get_no_cache_control_from_the_middleware()
    {
        using var response = await GetAsync("/spa-file");

        response.Headers.CacheControl.Should().BeNull();
    }

    [Fact]
    public async Task Reference_data_gets_an_etag_and_must_revalidate()
    {
        using var response = await GetAsync("/api/test-perf/reference");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.ETag!.IsWeak.Should().BeTrue();
        response.Headers.CacheControl!.Private.Should().BeTrue();
        response.Headers.CacheControl.NoCache.Should().BeTrue();
        response.Headers.CacheControl.NoStore.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reference_data_answers_304_when_unchanged(bool strongForm)
    {
        using var first = await GetAsync("/api/test-perf/reference");
        var etag = first.Headers.ETag!.ToString();
        var sent = strongForm ? etag[2..] : $"\"other\", {etag}";

        using var second = await GetAsync("/api/test-perf/reference", ifNoneMatch: sent);

        second.StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await second.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
        second.Headers.ETag!.ToString().Should().Be(etag);
    }

    [Fact]
    public async Task Reference_data_returns_the_body_when_the_etag_differs()
    {
        using var response = await GetAsync("/api/test-perf/reference?name=b", ifNoneMatch: "W/\"stale\"");
        using var other = await GetAsync("/api/test-perf/reference?name=c");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"b\"");
        other.Headers.ETag.Should().NotBe(response.Headers.ETag);
    }

    [Fact]
    public async Task Reference_data_errors_get_no_etag()
    {
        using var response = await GetAsync("/api/test-perf/reference-missing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.ETag.Should().BeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }
}

[ApiController]
[Route("api/test-perf")]
public sealed class TestPerformanceController : ControllerBase
{
    public sealed record Row(string Name, string? Note);

    internal static Row[] Large() =>
        Enumerable.Range(0, 500).Select(i => new Row($"Employee number {i} in the sales department", null)).ToArray();

    [HttpGet("large")]
    public IActionResult LargeList() => Ok(Large());

    [HttpGet("problem")]
    public IActionResult ProblemResult() => Problem(new string('x', 2000), statusCode: 400);

    [HttpGet("secret")]
    [NoResponseCompression]
    public IActionResult Secret() => Ok(new { accessToken = "token-value", padding = new string('x', 2000) });

    [HttpGet("nulls")]
    public IActionResult Nulls() => Ok(new Row("a", null));

    [HttpGet("own-cache")]
    public IActionResult OwnCache()
    {
        Response.Headers.CacheControl = "private, max-age=900";
        return Ok(new Row("a", null));
    }

    [HttpGet("reference")]
    [ReferenceDataCache]
    public IActionResult Reference([FromQuery] string name = "a") => Ok(new[] { new Row(name, "note") });

    [HttpGet("reference-missing")]
    [ReferenceDataCache]
    public IActionResult ReferenceMissing() => NotFound();
}
