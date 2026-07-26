using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Helpers;

namespace ONEVO.Infrastructure.ExternalServices.Email;

/// <summary>
/// Sends transactional email through the SendGrid v3 Mail Send API using a minimal
/// typed HttpClient (no SendGrid SDK dependency).
/// SECURITY: the Authorization header is built locally from the resolver-provided key,
/// is never logged, and provider error bodies are never stored - only the HTTP status
/// code is surfaced as a safe error.
/// </summary>
public sealed class SendGridEmailAdapter : IEmailProviderAdapter
{
    public const string HttpClientName = "email-sendgrid";
    private const string MailSendUrl = "https://api.sendgrid.com/v3/mail/send";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<SendGridEmailAdapter> _logger;

    public SendGridEmailAdapter(
        IHttpClientFactory httpClientFactory,
        IDateTimeProvider clock,
        ILogger<SendGridEmailAdapter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _clock = clock;
        _logger = logger;
    }

    public string Provider => PlatformServiceKeyCatalog.Sendgrid;

    public async Task<TransactionalEmailResult> SendAsync(
        string apiKey,
        EmailOptions options,
        TransactionalEmailRequest request,
        CancellationToken ct)
    {
        // SendGrid requires text/plain before text/html when both are present.
        var content = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.TextBody))
            content.Add(new { type = "text/plain", value = request.TextBody });
        content.Add(new { type = "text/html", value = request.HtmlBody });

        var payload = new
        {
            personalizations = new[]
            {
                new { to = new[] { new { email = request.RecipientEmail } } }
            },
            from = new { email = options.FromAddress, name = options.FromName },
            reply_to = string.IsNullOrWhiteSpace(options.ReplyToEmail)
                ? null
                : new { email = options.ReplyToEmail },
            subject = request.Subject,
            content
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, MailSendUrl);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
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
            _logger.LogWarning("SendGrid mail send request failed before a response: {ExceptionType}.",
                ex.GetType().Name);
            return TransactionalEmailResult.Failed(
                Provider, $"SendGrid request failed: {ex.GetType().Name}.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // Never store the response body - surface only the status code.
                _logger.LogWarning("SendGrid mail send returned {StatusCode} for recipient {Recipient}.",
                    (int)response.StatusCode, request.RecipientEmail);
                return TransactionalEmailResult.Failed(
                    Provider, $"SendGrid API returned {(int)response.StatusCode} {response.StatusCode}.");
            }

            string? providerMessageId = null;
            if (response.Headers.TryGetValues("X-Message-Id", out var values))
                providerMessageId = values.FirstOrDefault();

            return TransactionalEmailResult.Sent(Provider, providerMessageId, _clock.UtcNow);
        }
    }
}
