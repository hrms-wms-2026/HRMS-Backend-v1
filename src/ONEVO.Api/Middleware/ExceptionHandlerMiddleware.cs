using System.Net;
using System.Text.Json;
using FluentValidation;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Domain.Errors;

namespace ONEVO.Api.Middleware;

public class ExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlerMiddleware> _logger;

    public ExceptionHandlerMiddleware(RequestDelegate next, ILogger<ExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.Items["X-Correlation-Id"]?.ToString() ?? Guid.NewGuid().ToString();

        var (statusCode, title, detail, errors) = exception switch
        {
            ValidationException ve => (
                (int)HttpStatusCode.BadRequest,
                "Validation Error",
                "One or more validation errors occurred.",
                (object?)ve.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),

            NotFoundException ne => (
                (int)HttpStatusCode.NotFound,
                "Not Found",
                ne.Message,
                (object?)null),

            DomainException de => (
                (int)HttpStatusCode.UnprocessableEntity,
                "Business Rule Violation",
                de.Message,
                (object?)null),

            ForbiddenException fe => (
                (int)HttpStatusCode.Forbidden,
                "Forbidden",
                fe.Message,
                (object?)null),

            ServiceUnavailableException se => (
                (int)HttpStatusCode.ServiceUnavailable,
                "Service Unavailable",
                se.ErrorCode,
                (object?)null),

            _ => (
                (int)HttpStatusCode.InternalServerError,
                "Internal Server Error",
                "An unexpected error occurred.",
                (object?)null)
        };

        if (statusCode == (int)HttpStatusCode.InternalServerError)
            _logger.LogError(exception, "Unhandled exception. CorrelationId: {CorrelationId}", correlationId);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var problemDetails = new
        {
            type = $"https://onevo.com/errors/{title.ToLowerInvariant().Replace(" ", "-")}",
            title,
            status = statusCode,
            detail,
            instance = context.Request.Path.Value,
            errors,
            correlationId
        };

        var json = JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        await context.Response.WriteAsync(json);
    }
}
