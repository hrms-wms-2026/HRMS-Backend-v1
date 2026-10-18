using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Helpers;

namespace ONEVO.Infrastructure.ExternalServices.Email;

/// <summary>
/// Sends transactional email through the Resend HTTP API (POST /emails) using a minimal
/// typed HttpClient. Replaces the old Resend-over-SMTP path whose password came from
/// appsettings; the API key now always comes from platform_service_keys.
/// SECURITY: same rules as SendGrid - Authorization built locally, never logged,
/// provider error bodies never stored.
///
/// IDEMPOTENCY: Resend honours an Idempotency-Key header on POST /emails
/// (https://resend.com/docs/dashboard/emails/idempotency-keys): 1-256 characters, kept for
/// 24 hours; the same key with the same payload returns the original response without sending
/// again, the same key with a different payload is 409 invalid_idempotent_request, and a key whose
/// first request is still in flight is 409 concurrent_idempotent_requests. The outbox passes a key
/// derived from the message id (TransactionalEmailRequest.IdempotencyKey), so a retry after a crash
/// that followed Resend's acceptance does not send a second email within that 24-hour window.
/// </summary>
public sealed class ResendEmailAdapter : IEmailProviderAdapter
{
    public const string HttpClientName = "email-resend";
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string SendEmailUrl = "https://api.resend.com/emails";

    /// <summary>The only parts of a 409 body ever surfaced: Resend's documented idempotency error names.</summary>
    private static readonly HashSet<string> IdempotencyErrorNames = new(StringComparer.Ordinal)
    {
        "concurrent_idempotent_requests",
        "invalid_idempotent_request",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ResendEmailAdapter> _logger;

    public ResendEmailAdapter(
        IHttpClientFactory httpClientFactory,
        IDateTimeProvider clock,
        ILogger<ResendEmailAdapter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _clock = clock;
        _logger = logger;
    }

    public string Provider => PlatformServiceKeyCatalog.Resend;

    public async Task<TransactionalEmailResult> SendAsync(
        string apiKey,
        EmailOptions options,
        TransactionalEmailRequest request,
        CancellationToken ct)
    {
        var payload = new
        {
            from = $"{options.FromName} <{options.FromAddress}>",
            to = new[] { request.RecipientEmail },
            subject = request.Subject,
            html = request.HtmlBody,
            text = string.IsNullOrWhiteSpace(request.TextBody) ? null : request.TextBody,
            reply_to = string.IsNullOrWhiteSpace(options.ReplyToEmail) ? null : options.ReplyToEmail,
            attachments = request.Attachments is { Count: > 0 } files
                ? files.Select(a => new { filename = a.FileName, content = Convert.ToBase64String(a.Content), content_type = a.ContentType }).ToArray()
                : null
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, SendEmailUrl);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            httpRequest.Headers.TryAddWithoutValidation(IdempotencyKeyHeader, request.IdempotencyKey);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            response = await client.SendAsync(httpRequest, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Resend email send request failed before a response: {ExceptionType}.",
                ex.GetType().Name);
            return TransactionalEmailResult.Failed(
                Provider, $"Resend request failed: {ex.GetType().Name}.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var idempotencyError = response.StatusCode == System.Net.HttpStatusCode.Conflict
                    ? await ReadIdempotencyErrorNameAsync(response, ct)
                    : null;
                _logger.LogWarning("Resend email send returned {StatusCode}{IdempotencyError} for recipient {Recipient}.",
                    (int)response.StatusCode, idempotencyError is null ? "" : $" ({idempotencyError})", request.RecipientEmail);
                // A 409 concurrent_idempotent_requests is retryable (the outbox backs off and retries);
                // invalid_idempotent_request means the payload changed under the same key.
                return TransactionalEmailResult.Failed(
                    Provider, idempotencyError is null
                        ? $"Resend API returned {(int)response.StatusCode} {response.StatusCode}."
                        : $"Resend API returned {(int)response.StatusCode} {response.StatusCode} ({idempotencyError}).");
            }

            string? providerMessageId = null;
            try
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("id", out var idElement))
                    providerMessageId = idElement.GetString();
            }
            catch (JsonException)
            {
                // Success without a parseable id is still a successful send.
            }

            return TransactionalEmailResult.Sent(Provider, providerMessageId, _clock.UtcNow);
        }
    }

    /// <summary>Returns the error name only when it is one of Resend's documented idempotency
    /// errors; the rest of the provider body is never surfaced or stored.</summary>
    private static async Task<string?> ReadIdempotencyErrorNameAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("name", out var name)
                && name.ValueKind == JsonValueKind.String
                && IdempotencyErrorNames.Contains(name.GetString()!)
                    ? name.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
