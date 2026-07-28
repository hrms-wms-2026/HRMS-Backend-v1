using System.IO;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using ONEVO.Api.Middleware;
using ONEVO.Application.Common.Exceptions;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Api.Middleware;

public class ExceptionHandlerMiddlewareTests
{
    private readonly Mock<ILogger<ExceptionHandlerMiddleware>> _logger = new();

    private ExceptionHandlerMiddleware BuildMiddleware(RequestDelegate next) =>
        new(next, _logger.Object);

    private static HttpContext CreateHttpContext(string path = "/test/path")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonElement> ReadResponseBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public async Task InvokeAsync_WhenValidationExceptionThrown_Returns400BadRequestWithProblemDetails()
    {
        var failures = new List<ValidationFailure>
        {
            new("Reason", "reason is required when suspending a tenant.")
        };
        var middleware = BuildMiddleware(_ => throw new ValidationException(failures));
        var context = CreateHttpContext("/admin/v1/tenants/status");
        context.Items["X-Correlation-Id"] = "test-corr-id-123";

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        context.Response.ContentType.Should().Be("application/problem+json");

        var body = await ReadResponseBodyAsync(context);
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.GetProperty("title").GetString().Should().Be("Validation Error");
        body.GetProperty("detail").GetString().Should().Be("One or more validation errors occurred.");
        body.GetProperty("instance").GetString().Should().Be("/admin/v1/tenants/status");
        body.GetProperty("correlationId").GetString().Should().Be("test-corr-id-123");
        body.GetProperty("errors").GetProperty("Reason").EnumerateArray()
            .Select(e => e.GetString()).Should().Contain("reason is required when suspending a tenant.");

        // Must not log error for validation failures (not an unhandled server error)
        _logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_WhenUnexpectedExceptionThrown_Returns500InternalServerErrorAndLogsError()
    {
        var middleware = BuildMiddleware(_ => throw new InvalidOperationException("Sensitive DB Error"));
        var context = CreateHttpContext("/admin/v1/tenants/status");
        context.Items["X-Correlation-Id"] = "test-corr-id-500";

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        context.Response.ContentType.Should().Be("application/problem+json");

        var body = await ReadResponseBodyAsync(context);
        body.GetProperty("status").GetInt32().Should().Be(500);
        body.GetProperty("title").GetString().Should().Be("Internal Server Error");
        body.GetProperty("detail").GetString().Should().Be("An unexpected error occurred.");
        body.GetProperty("correlationId").GetString().Should().Be("test-corr-id-500");

        // Must NOT leak internal exception message or stack trace in body
        var rawJson = body.ToString();
        rawJson.Should().NotContain("Sensitive DB Error");
        rawJson.Should().NotContain("StackTrace");

        // Must log error for internal server errors
        _logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WhenNotFoundExceptionThrown_Returns404NotFound()
    {
        var middleware = BuildMiddleware(_ => throw new NotFoundException("Tenant not found"));
        var context = CreateHttpContext("/admin/v1/tenants/missing-id");

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
        context.Response.ContentType.Should().Be("application/problem+json");

        var body = await ReadResponseBodyAsync(context);
        body.GetProperty("status").GetInt32().Should().Be(404);
        body.GetProperty("detail").GetString().Should().Be("Tenant not found");
    }
}
