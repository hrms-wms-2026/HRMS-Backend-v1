using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using ONEVO.Api.Configuration;
using ONEVO.Api.Filters;
using ONEVO.Api.Middleware;

namespace ONEVO.Tests.Unit.Features.Api.Middleware;

/// <summary>Task 23: security headers, the global body limit and file response headers.</summary>
public sealed class Task23SecurityHardeningTests
{
    // ── SecurityHeadersMiddleware ────────────────────────────────────────────

    [Fact]
    public async Task SecurityHeaders_AddsTheFullHeaderSet()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/auth/me";
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask, Settings());

        await middleware.InvokeAsync(context);

        var headers = context.Response.Headers;
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", headers["X-Frame-Options"]);
        Assert.Equal("no-referrer", headers["Referrer-Policy"]);
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", headers["Content-Security-Policy"]);
        Assert.Equal("same-origin", headers["Cross-Origin-Opener-Policy"]);
        Assert.Equal("same-origin", headers["Cross-Origin-Resource-Policy"]);
        var permissions = headers["Permissions-Policy"].ToString();
        foreach (var feature in new[] { "camera=()", "microphone=()", "geolocation=()", "payment=()", "usb=()" })
            Assert.Contains(feature, permissions);
    }

    [Fact]
    public async Task SecurityHeaders_SpaPageWhenServingSpas_GetsSpaCspWithoutTheApiOnlyPolicies()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/dashboard";
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask, Settings(serveSpas: true));

        await middleware.InvokeAsync(context);

        var headers = context.Response.Headers;
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", headers["X-Frame-Options"]);
        Assert.Equal(SecurityHeadersMiddleware.SpaCsp, headers["Content-Security-Policy"]);
        Assert.False(headers.ContainsKey("Permissions-Policy"));
        Assert.False(headers.ContainsKey("Cross-Origin-Opener-Policy"));
        Assert.False(headers.ContainsKey("Cross-Origin-Resource-Policy"));
    }

    [Fact]
    public async Task SecurityHeaders_ApiPathWhenServingSpas_KeepsTheFullApiHeaderSet()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/auth/me";
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask, Settings(serveSpas: true));

        await middleware.InvokeAsync(context);

        var headers = context.Response.Headers;
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", headers["Content-Security-Policy"]);
        Assert.Equal(SecurityHeadersMiddleware.PermissionsPolicy, headers["Permissions-Policy"]);
        Assert.Equal("same-origin", headers["Cross-Origin-Resource-Policy"]);
    }

    private static IConfiguration Settings(bool serveSpas = false) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ONEVO.Api.Extensions.SpaHostingExtensions.ServeSpasKey] = serveSpas ? "true" : "false",
            })
            .Build();

    // ── RequestBodySizeLimitMiddleware ───────────────────────────────────────

    [Fact]
    public void ResolveLimit_EndpointWithoutItsOwnLimit_GetsTheOneMegabyteDefault()
    {
        var endpoint = new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "normal");

        Assert.Equal(1024 * 1024, RequestBodySizeLimitMiddleware.ResolveLimit(endpoint));
        Assert.Equal(1024 * 1024, RequestBodySizeLimitMiddleware.ResolveLimit(endpoint: null));
    }

    [Fact]
    public void ResolveLimit_EndpointWithRequestSizeLimit_KeepsItsOwnLimit()
    {
        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new RequestSizeLimitAttribute(26 * 1024 * 1024)),
            "upload");

        Assert.Equal(26 * 1024 * 1024, RequestBodySizeLimitMiddleware.ResolveLimit(endpoint));
    }

    [Fact]
    public async Task BodyLimit_ContentLengthOverTheLimit_Returns413WithoutCallingTheEndpoint()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentLength = 2 * 1024 * 1024;
        context.Response.Body = new MemoryStream();
        var called = false;
        var middleware = new RequestBodySizeLimitMiddleware(_ => { called = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        Assert.False(called);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
    }

    [Fact]
    public async Task BodyLimit_SetsTheServerLimitForChunkedBodies()
    {
        var feature = new MaxBodySizeFeature();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new RequestSizeLimitAttribute(10 * 1024 * 1024)),
            "screenshot"));
        var middleware = new RequestBodySizeLimitMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal(10 * 1024 * 1024, feature.MaxRequestBodySize);
    }

    [Fact]
    public async Task BodyLimit_UnderTheLimit_CallsTheEndpoint()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentLength = 512 * 1024;
        var called = false;
        var middleware = new RequestBodySizeLimitMiddleware(_ => { called = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        Assert.True(called);
    }

    [Fact]
    public async Task ChunkedBodyPastTheLimit_KestrelsBadRequestBecomesA413()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlerMiddleware(
            _ => throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ExceptionHandlerMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
    }

    /// <summary>
    /// Every action that binds an uploaded file must declare its own [RequestSizeLimit]; otherwise it
    /// silently gets the 1 MB default and real uploads start failing with 413.
    /// </summary>
    [Fact]
    public void EveryActionThatAcceptsAFile_DeclaresItsOwnRequestSizeLimit()
    {
        var actions = typeof(SecurityHeadersMiddleware).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(m => m.GetParameters().Any(p => TakesAFile(p.ParameterType)))
            .ToList();

        Assert.NotEmpty(actions);
        var missing = actions
            .Where(m => m.GetCustomAttribute<RequestSizeLimitAttribute>() is null)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();
        Assert.True(missing.Count == 0, "Upload actions without [RequestSizeLimit]: " + string.Join(", ", missing));
    }

    private static bool TakesAFile(Type type) =>
        typeof(IFormFile).IsAssignableFrom(type)
        || type.GetProperties().Any(p => typeof(IFormFile).IsAssignableFrom(p.PropertyType));

    [Fact]
    public void RequestBodyLimits_NeverGoBelowTheFilesTheyCarry()
    {
        Assert.True(RequestBodyLimits.DocumentUpload > 25 * 1024 * 1024);
        Assert.True(RequestBodyLimits.ProjectCreateWithImages > 10 * 1024 * 1024);
        Assert.True(RequestBodyLimits.AppCatalogLogo > 2 * 1024 * 1024);
        Assert.True(RequestBodyLimits.BulkOnboardingFile > RequestBodyLimits.BulkOnboardingFileMaxBytes);
    }

    // ── UserFileResponseFilter ───────────────────────────────────────────────

    [Theory]
    [InlineData("application/pdf", "attachment")]
    [InlineData("text/html; charset=utf-8", "attachment")]
    [InlineData("image/svg+xml", "attachment")]
    [InlineData("image/gif", "attachment")]
    [InlineData("application/octet-stream", "attachment")]
    [InlineData("image/png", "inline")]
    [InlineData("image/jpeg", "inline")]
    [InlineData("image/webp", "inline")]
    public async Task FileFilter_SetsDispositionNosniffSandboxAndSameSiteCorp(string contentType, string disposition)
    {
        var (context, headers) = ResultContext(new FileContentResult(new byte[] { 1 }, contentType));

        await new UserFileResponseFilter().OnResultExecutionAsync(context, () => Task.FromResult<ResultExecutedContext>(null!));

        Assert.Equal(disposition, headers["Content-Disposition"]);
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal(UserFileResponseFilter.FileCsp, headers["Content-Security-Policy"]);
        Assert.Equal("same-site", headers["Cross-Origin-Resource-Policy"]);
    }

    [Fact]
    public async Task FileFilter_NamedDownload_LeavesMvcsAttachmentHeaderAlone()
    {
        var (context, headers) = ResultContext(
            new FileContentResult(new byte[] { 1 }, "text/csv") { FileDownloadName = "report.csv" });

        await new UserFileResponseFilter().OnResultExecutionAsync(context, () => Task.FromResult<ResultExecutedContext>(null!));

        Assert.False(headers.ContainsKey("Content-Disposition"));
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
    }

    [Fact]
    public async Task FileFilter_JsonResult_IsUntouched()
    {
        var (context, headers) = ResultContext(new OkObjectResult(new { ok = true }));

        await new UserFileResponseFilter().OnResultExecutionAsync(context, () => Task.FromResult<ResultExecutedContext>(null!));

        Assert.False(headers.ContainsKey("Content-Disposition"));
        Assert.False(headers.ContainsKey("Cross-Origin-Resource-Policy"));
    }

    private static (ResultExecutingContext Context, IHeaderDictionary Headers) ResultContext(IActionResult result)
    {
        var http = new DefaultHttpContext();
        var action = new ActionContext(http, new RouteData(), new ControllerActionDescriptor());
        var context = new ResultExecutingContext(action, new List<IFilterMetadata>(), result, controller: new object());
        return (context, http.Response.Headers);
    }

    private sealed class MaxBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;
        public long? MaxRequestBodySize { get; set; } = 30_000_000;
    }
}
