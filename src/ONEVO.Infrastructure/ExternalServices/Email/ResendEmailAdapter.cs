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
/// SECURITY: same rules as SendGrid — Authorization built locally, never logged,
/// provider error bodies never stored.
/// </summary>
public sealed class ResendEmailAdapter : IEmailProviderAdapter
{
    public const string HttpClientName = "email-resend";
    private const string SendEmailUrl = "https://api.resend.com/emails";

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
            reply_to = string.IsNullOrWhiteSpace(options.ReplyToEmail) ? null : options.ReplyToEmail
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, SendEmailUrl);
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
            _logger.LogWarning("Resend email send request failed before a response: {ExceptionType}.",
                ex.GetType().Name);
            return TransactionalEmailResult.Failed(
                Provider, $"Resend request failed: {ex.GetType().Name}.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Resend email send returned {StatusCode} for recipient {Recipient}.",
                    (int)response.StatusCode, request.RecipientEmail);
                return TransactionalEmailResult.Failed(
                    Provider, $"Resend API returned {(int)response.StatusCode} {response.StatusCode}.");
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
}
