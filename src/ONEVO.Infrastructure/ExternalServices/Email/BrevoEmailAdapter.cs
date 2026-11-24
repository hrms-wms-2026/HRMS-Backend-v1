using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Helpers;

namespace ONEVO.Infrastructure.ExternalServices.Email;

/// <summary>
/// Sends transactional email through the Brevo v3 transactional email API
/// (POST /v3/smtp/email, https://developers.brevo.com/reference/sendtransacemail) using a
/// minimal typed HttpClient (no Brevo SDK dependency).
/// SECURITY: same rules as SendGrid and Resend - the api-key header is built locally from the
/// resolver-provided key, is never logged, and provider error bodies are never stored; only the
/// HTTP status code is surfaced as a safe error.
///
/// IDEMPOTENCY: none relied on. Brevo documents no idempotency guarantee for this endpoint, so
/// TransactionalEmailRequest.IdempotencyKey is deliberately not sent. Like SendGrid, a crash after
/// Brevo accepted a message but before the outbox row is marked Published can cause a duplicate
/// when the outbox retries (at-least-once).
/// </summary>
public sealed class BrevoEmailAdapter : IEmailProviderAdapter
{
    public const string HttpClientName = "email-brevo";
    public const string ApiKeyHeader = "api-key";
    private const string SendEmailUrl = "https://api.brevo.com/v3/smtp/email";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<BrevoEmailAdapter> _logger;

    public BrevoEmailAdapter(
        IHttpClientFactory httpClientFactory,
        IDateTimeProvider clock,
        ILogger<BrevoEmailAdapter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _clock = clock;
        _logger = logger;
    }

    public string Provider => PlatformServiceKeyCatalog.Brevo;

    public async Task<TransactionalEmailResult> SendAsync(
        string apiKey,
        EmailOptions options,
        TransactionalEmailRequest request,
        CancellationToken ct)
    {
        var payload = new
        {
            sender = new { name = options.FromName, email = options.FromAddress },
            to = new[] { new { email = request.RecipientEmail } },
            replyTo = string.IsNullOrWhiteSpace(options.ReplyToEmail)
                ? null
                : new { email = options.ReplyToEmail },
            subject = request.Subject,
            htmlContent = request.HtmlBody,
            textContent = string.IsNullOrWhiteSpace(request.TextBody) ? null : request.TextBody,
            // Brevo infers the attachment type from the file name's extension.
            attachment = request.Attachments is { Count: > 0 } files
                ? files.Select(a => new { content = Convert.ToBase64String(a.Content), name = a.FileName }).ToArray()
                : null
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, SendEmailUrl);
        httpRequest.Headers.TryAddWithoutValidation(ApiKeyHeader, apiKey);
        httpRequest.Headers.Accept.ParseAdd("application/json");
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
            _logger.LogWarning("Brevo email send request failed before a response: {ExceptionType}.",
                ex.GetType().Name);
            return TransactionalEmailResult.Failed(
                Provider, $"Brevo request failed: {ex.GetType().Name}.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // Never store the response body - surface only the status code.
                _logger.LogWarning("Brevo email send returned {StatusCode} for recipient {Recipient}.",
                    (int)response.StatusCode, ONEVO.Application.Common.Observability.LogRedaction.MaskEmail(request.RecipientEmail));
                return TransactionalEmailResult.Failed(
                    Provider, $"Brevo API returned {(int)response.StatusCode} {response.StatusCode}.");
            }

            string? providerMessageId = null;
            try
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("messageId", out var idElement)
                    && idElement.ValueKind == JsonValueKind.String)
                    providerMessageId = idElement.GetString();
            }
            catch (JsonException)
            {
                // Success without a parseable id is still a successful send.
            }

            return TransactionalEmailResult.Sent(Provider, providerMessageId, _clock.UtcNow);
        }
    }
}
