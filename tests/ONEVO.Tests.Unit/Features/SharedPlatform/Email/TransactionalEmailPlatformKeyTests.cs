using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.OutboxHandlers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Infrastructure.ExternalServices.Email;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Email;

/// <summary>
/// Transactional email wiring against platform_service_keys:
/// provider selection from non-secret Email:Provider config, key resolution through
/// IPlatformServiceKeyResolver, safe failure when no active key exists, provider
/// adapters (fake HttpClient, no real network), and no-secret-leak guarantees.
/// </summary>
public class TransactionalEmailPlatformKeyTests
{
    private const string FakeKey = "SG.fake-unit-test-key-value";

    private static TransactionalEmailRequest Request(string to = "user@example.com") =>
        new(TenantId: null, RecipientEmail: to, Subject: "Hello",
            HtmlBody: "<p>Hi</p>", TextBody: "Hi");

    private static EmailOptions Options(string provider = "sendgrid") => new()
    {
        Provider = provider,
        FromAddress = "no-reply@onevo.io",
        FromName = "ONEVO",
        ReplyToEmail = "support@onevo.io"
    };

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeResolver : IPlatformServiceKeyResolver
    {
        public List<string> RequestedSlugs { get; } = new();
        public string? KeyToReturn { get; set; } = FakeKey;

        public Task<string?> ResolveActiveKeyAsync(string serviceKey, CancellationToken ct)
        {
            RequestedSlugs.Add(serviceKey);
            return Task.FromResult(KeyToReturn);
        }
    }

    private sealed class FakeAdapter : IEmailProviderAdapter
    {
        public FakeAdapter(string provider) => Provider = provider;

        public string Provider { get; }
        public List<(string ApiKey, TransactionalEmailRequest Request)> Calls { get; } = new();
        public TransactionalEmailResult ResultToReturn { get; set; } =
            TransactionalEmailResult.Sent("fake", "msg-1", DateTimeOffset.UtcNow);

        public Task<TransactionalEmailResult> SendAsync(
            string apiKey, EmailOptions options, TransactionalEmailRequest request, CancellationToken ct)
        {
            Calls.Add((apiKey, request));
            return Task.FromResult(ResultToReturn);
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public int CallCount { get; private set; }

        public CapturingHandler(HttpResponseMessage response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _response;
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static IDateTimeProvider FixedClock(DateTimeOffset at)
    {
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(at);
        return clock.Object;
    }

    private static PlatformKeyTransactionalEmailSender BuildSender(
        FakeResolver resolver, EmailOptions options, params IEmailProviderAdapter[] adapters)
        => new(resolver, adapters, Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<PlatformKeyTransactionalEmailSender>.Instance);

    // ── 1. Provider key resolution ───────────────────────────────────────────

    [Fact]
    public async Task Sender_Requests_Sendgrid_Key_When_Provider_Is_Sendgrid()
    {
        var resolver = new FakeResolver();
        var adapter = new FakeAdapter("sendgrid");
        var sender = BuildSender(resolver, Options("sendgrid"), adapter);

        var result = await sender.SendAsync(Request());

        Assert.Equal(new[] { "sendgrid" }, resolver.RequestedSlugs);
        Assert.Single(adapter.Calls);
        Assert.Equal(FakeKey, adapter.Calls[0].ApiKey);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Sender_Requests_Resend_Key_When_Provider_Is_Resend()
    {
        var resolver = new FakeResolver();
        var adapter = new FakeAdapter("resend");
        var sender = BuildSender(resolver, Options("resend"), adapter);

        await sender.SendAsync(Request());

        Assert.Equal(new[] { "resend" }, resolver.RequestedSlugs);
        Assert.Single(adapter.Calls);
    }

    [Fact]
    public async Task Sender_Defaults_To_Sendgrid_When_Provider_Empty()
    {
        var resolver = new FakeResolver();
        var adapter = new FakeAdapter("sendgrid");
        var sender = BuildSender(resolver, Options(provider: ""), adapter);

        await sender.SendAsync(Request());

        Assert.Equal(new[] { "sendgrid" }, resolver.RequestedSlugs);
    }

    [Fact]
    public async Task Missing_Active_Key_Fails_Safely_And_Does_Not_Call_Provider()
    {
        var resolver = new FakeResolver { KeyToReturn = null };
        var adapter = new FakeAdapter("sendgrid");
        var sender = BuildSender(resolver, Options("sendgrid"), adapter);

        var result = await sender.SendAsync(Request());

        Assert.False(result.Success);
        Assert.Empty(adapter.Calls);
        Assert.Equal(
            "Active platform service key not configured for provider 'sendgrid'.",
            result.SafeError);
        Assert.Null(result.ProviderMessageId);
        Assert.Null(result.SentAt);
    }

    [Fact]
    public async Task Unsupported_Provider_Fails_Safely_Without_Resolving_Key()
    {
        var resolver = new FakeResolver();
        var sender = BuildSender(resolver, Options("smtp"), new FakeAdapter("sendgrid"));

        var result = await sender.SendAsync(Request());

        Assert.False(result.Success);
        Assert.Empty(resolver.RequestedSlugs);
        Assert.Contains("Unsupported email provider 'smtp'", result.SafeError);
    }

    [Fact]
    public async Task Missing_FromAddress_Fails_Safely_Without_Resolving_Key()
    {
        var resolver = new FakeResolver();
        var options = Options("sendgrid");
        options.FromAddress = "";
        var sender = BuildSender(resolver, options, new FakeAdapter("sendgrid"));

        var result = await sender.SendAsync(Request());

        Assert.False(result.Success);
        Assert.Empty(resolver.RequestedSlugs);
        Assert.Equal("Email:FromAddress is not configured.", result.SafeError);
    }

    // ── 2. SendGrid adapter ──────────────────────────────────────────────────

    [Fact]
    public async Task SendGridAdapter_Builds_Authorized_Request_And_Captures_MessageId()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Accepted);
        response.Headers.Add("X-Message-Id", "sg-msg-123");
        var handler = new CapturingHandler(response);
        var sentAt = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var adapter = new SendGridEmailAdapter(
            new FakeHttpClientFactory(handler), FixedClock(sentAt),
            NullLogger<SendGridEmailAdapter>.Instance);

        var result = await adapter.SendAsync(FakeKey, Options(), Request("dest@example.com"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("sendgrid", result.Provider);
        Assert.Equal("sg-msg-123", result.ProviderMessageId);
        Assert.Equal(sentAt, result.SentAt);
        Assert.Null(result.SafeError);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("https://api.sendgrid.com/v3/mail/send", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal(FakeKey, handler.LastRequest.Headers.Authorization.Parameter);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var root = body.RootElement;
        Assert.Equal("dest@example.com",
            root.GetProperty("personalizations")[0].GetProperty("to")[0].GetProperty("email").GetString());
        Assert.Equal("no-reply@onevo.io", root.GetProperty("from").GetProperty("email").GetString());
        Assert.Equal("ONEVO", root.GetProperty("from").GetProperty("name").GetString());
        Assert.Equal("Hello", root.GetProperty("subject").GetString());
        Assert.Equal("text/plain", root.GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("Hi", root.GetProperty("content")[0].GetProperty("value").GetString());
        Assert.Equal("text/html", root.GetProperty("content")[1].GetProperty("type").GetString());
        Assert.Equal("<p>Hi</p>", root.GetProperty("content")[1].GetProperty("value").GetString());
        // The key must never appear in the request body.
        Assert.DoesNotContain(FakeKey, handler.LastRequestBody);
    }

    [Fact]
    public async Task SendGridAdapter_NonSuccess_Status_Returns_Safe_Error_Without_Key()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"errors\":[{\"message\":\"bad key\"}]}")
        };
        var adapter = new SendGridEmailAdapter(
            new FakeHttpClientFactory(new CapturingHandler(response)),
            FixedClock(DateTimeOffset.UtcNow), NullLogger<SendGridEmailAdapter>.Instance);

        var result = await adapter.SendAsync(FakeKey, Options(), Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("401", result.SafeError);
        Assert.DoesNotContain(FakeKey, result.SafeError);
        // Provider response body must not be stored either.
        Assert.DoesNotContain("bad key", result.SafeError);
    }

    // ── 3. Resend adapter ────────────────────────────────────────────────────

    [Fact]
    public async Task ResendAdapter_Builds_Authorized_Request_And_Parses_Id()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"id\":\"re-msg-9\"}")
        };
        var handler = new CapturingHandler(response);
        var adapter = new ResendEmailAdapter(
            new FakeHttpClientFactory(handler), FixedClock(DateTimeOffset.UtcNow),
            NullLogger<ResendEmailAdapter>.Instance);

        var result = await adapter.SendAsync(FakeKey, Options(), Request("dest@example.com"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("resend", result.Provider);
        Assert.Equal("re-msg-9", result.ProviderMessageId);
        Assert.Equal("https://api.resend.com/emails", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal(FakeKey, handler.LastRequest.Headers.Authorization!.Parameter);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("dest@example.com", body.RootElement.GetProperty("to")[0].GetString());
        Assert.Equal("Hello", body.RootElement.GetProperty("subject").GetString());
        Assert.Equal("<p>Hi</p>", body.RootElement.GetProperty("html").GetString());
    }

    [Fact]
    public async Task ResendAdapter_NonSuccess_Status_Returns_Safe_Error_Without_Key()
    {
        var response = new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("{\"message\":\"invalid\"}")
        };
        var adapter = new ResendEmailAdapter(
            new FakeHttpClientFactory(new CapturingHandler(response)),
            FixedClock(DateTimeOffset.UtcNow), NullLogger<ResendEmailAdapter>.Instance);

        var result = await adapter.SendAsync(FakeKey, Options("resend"), Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("422", result.SafeError);
        Assert.DoesNotContain(FakeKey, result.SafeError);
    }

    // ── 4. IEmailService wrapper (outbox-facing behavior) ───────────────────

    private sealed class FakeSender : ITransactionalEmailSender
    {
        public List<TransactionalEmailRequest> Requests { get; } = new();
        public TransactionalEmailResult ResultToReturn { get; set; } =
            TransactionalEmailResult.Sent("sendgrid", "id-1", DateTimeOffset.UtcNow);

        public Task<TransactionalEmailResult> SendAsync(TransactionalEmailRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(ResultToReturn);
        }
    }

    private static TransactionalEmailService BuildEmailService(FakeSender sender)
        => new(sender,
            new EmailTemplateRenderer(Microsoft.Extensions.Options.Options.Create(
                new EmailOptions { AppBaseUrl = "http://localhost:5173" })),
            NullLogger<TransactionalEmailService>.Instance);

    [Fact]
    public async Task EmailService_Success_Completes_Without_Throwing()
    {
        var sender = new FakeSender();
        var service = BuildEmailService(sender);

        await service.SendAsync("user@example.com", "Subject", "<p>Body</p>");

        var request = Assert.Single(sender.Requests);
        Assert.Equal("user@example.com", request.RecipientEmail);
        Assert.Equal("Subject", request.Subject);
    }

    [Fact]
    public async Task EmailService_Failure_Throws_Safe_Error_So_Outbox_Retries()
    {
        var sender = new FakeSender
        {
            ResultToReturn = TransactionalEmailResult.Failed(
                "sendgrid", "Active platform service key not configured for provider 'sendgrid'.")
        };
        var service = BuildEmailService(sender);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendAsync("user@example.com", "Subject", "<p>Body</p>"));

        // The message the outbox stores as last_error must be the safe error only.
        Assert.Equal("Active platform service key not configured for provider 'sendgrid'.", ex.Message);
        Assert.DoesNotContain(FakeKey, ex.Message);
    }

    [Fact]
    public async Task EmailService_PasswordReset_Renders_Template_And_Sends()
    {
        var sender = new FakeSender();
        var service = BuildEmailService(sender);

        await service.SendPasswordResetAsync("user@example.com", "tok-123");

        var request = Assert.Single(sender.Requests);
        Assert.Equal("Reset your ONEVO password", request.Subject);
        Assert.Contains("tok-123", request.HtmlBody);
    }

    // ── 5. Outbox invite handler uses the platform-key path end to end ──────

    [Fact]
    public async Task InviteOutboxHandler_Queued_Email_Is_Sent_Through_Platform_Key_Sender()
    {
        var sender = new FakeSender();
        var service = BuildEmailService(sender);
        var tenants = new Mock<ITenantRepository>();
        var handler = new TenantOwnerInviteEmailOutboxHandler(service, tenants.Object);

        var payload = new TenantOwnerInviteEmailPayload(
            Guid.NewGuid(), "Acme Corp", "owner@acme.test", "Ada",
            "invite-token-1", DateTimeOffset.UtcNow.AddDays(3));

        await handler.HandleAsync(JsonSerializer.Serialize(payload), CancellationToken.None);

        var request = Assert.Single(sender.Requests);
        Assert.Equal("owner@acme.test", request.RecipientEmail);
        Assert.Contains("Acme Corp", request.Subject);
        Assert.Contains("invite-token-1", request.HtmlBody);
    }

    [Fact]
    public async Task InviteOutboxHandler_Failed_Send_Propagates_Safe_Error_Only()
    {
        var sender = new FakeSender
        {
            ResultToReturn = TransactionalEmailResult.Failed(
                "sendgrid", "SendGrid API returned 401 Unauthorized.")
        };
        var service = BuildEmailService(sender);
        var handler = new TenantOwnerInviteEmailOutboxHandler(service, new Mock<ITenantRepository>().Object);

        var payload = new TenantOwnerInviteEmailPayload(
            Guid.NewGuid(), "Acme Corp", "owner@acme.test", "Ada",
            "invite-token-1", DateTimeOffset.UtcNow.AddDays(3));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(JsonSerializer.Serialize(payload), CancellationToken.None));

        Assert.Equal("SendGrid API returned 401 Unauthorized.", ex.Message);
        Assert.DoesNotContain(FakeKey, ex.Message);
    }

    // ── 6. No-secret-shape guarantees ────────────────────────────────────────

    [Theory]
    [InlineData(typeof(TransactionalEmailRequest))]
    [InlineData(typeof(TransactionalEmailResult))]
    [InlineData(typeof(EmailOptions))]
    public void Email_Contracts_Expose_No_Secret_Shaped_Properties(Type type)
    {
        foreach (var property in type.GetProperties())
        {
            var name = property.Name.ToLowerInvariant();
            Assert.False(
                name.Contains("apikey") || name.Contains("secret") ||
                name.Contains("password") || name.Contains("token") ||
                name.Contains("encrypted"),
                $"{type.Name}.{property.Name} looks like a secret field and must not exist.");
        }
    }

    [Fact]
    public void EmailOptions_No_Longer_Carries_Smtp_Or_Resend_Credential_Sections()
    {
        var propertyNames = typeof(EmailOptions).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("Smtp", propertyNames);
        Assert.DoesNotContain("Resend", propertyNames);
    }
}
