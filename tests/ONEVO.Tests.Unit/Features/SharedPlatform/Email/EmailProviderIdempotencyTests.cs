using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.ExternalServices.Email;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Email;

/// <summary>
/// Provider idempotency for outbox-dispatched email. Resend documents an Idempotency-Key header
/// (https://resend.com/docs/dashboard/emails/idempotency-keys: 1-256 chars, kept 24 h, same key +
/// same payload returns the original response without sending again). SendGrid's v3 Mail Send
/// documents no idempotency mechanism
/// (https://www.twilio.com/docs/sendgrid/api-reference/mail-send/mail-send), so no key is sent
/// there and a crash-after-send retry can duplicate.
/// </summary>
public sealed class EmailProviderIdempotencyTests
{
    private const string FakeKey = "re_fake-unit-test-key";

    private sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeSender : ITransactionalEmailSender
    {
        public List<TransactionalEmailRequest> Requests { get; } = new();

        public Task<TransactionalEmailResult> SendAsync(TransactionalEmailRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(TransactionalEmailResult.Sent("resend", "id-1", DateTimeOffset.UtcNow));
        }
    }

    private static EmailOptions Options() => new() { FromAddress = "no-reply@onevo.io", FromName = "ONEVO" };

    private static TransactionalEmailRequest Request(string? idempotencyKey) =>
        new(TenantId: null, RecipientEmail: "dest@example.com", Subject: "Hello", HtmlBody: "<p>Hi</p>", TextBody: null,
            IdempotencyKey: idempotencyKey);

    private static IDateTimeProvider Clock() => Mock.Of<IDateTimeProvider>(c => c.UtcNow == DateTimeOffset.UtcNow);

    private static ResendEmailAdapter Resend(CapturingHandler handler) =>
        new(new FakeHttpClientFactory(handler), Clock(), NullLogger<ResendEmailAdapter>.Instance);

    [Fact]
    public async Task ResendAdapter_SendsTheRequestsIdempotencyKey_AsTheIdempotencyKeyHeader()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"re-1\"}") });

        var result = await Resend(handler).SendAsync(FakeKey, Options(), Request("outbox/abc/1"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(handler.LastRequest!.Headers.TryGetValues("Idempotency-Key", out var values));
        Assert.Equal("outbox/abc/1", Assert.Single(values));
    }

    [Fact]
    public async Task ResendAdapter_WithoutAKey_SendsNoIdempotencyKeyHeader()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"re-1\"}") });

        await Resend(handler).SendAsync(FakeKey, Options(), Request(idempotencyKey: null), CancellationToken.None);

        Assert.False(handler.LastRequest!.Headers.Contains("Idempotency-Key"));
    }

    [Theory]
    [InlineData("concurrent_idempotent_requests")]
    [InlineData("invalid_idempotent_request")]
    public async Task ResendAdapter_IdempotencyConflict_FailsWithTheDocumentedErrorName_AndNothingElseFromTheBody(string errorName)
    {
        var body = $"{{\"name\":\"{errorName}\",\"message\":\"secret-ish provider detail\",\"statusCode\":409}}";
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent(body) });

        var result = await Resend(handler).SendAsync(FakeKey, Options(), Request("outbox/abc/1"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("409", result.SafeError);
        Assert.Contains(errorName, result.SafeError);
        Assert.DoesNotContain("secret-ish", result.SafeError);
        Assert.DoesNotContain(FakeKey, result.SafeError);
    }

    [Fact]
    public async Task ResendAdapter_UnrecognisedConflictBody_IsNotEchoed()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("{\"name\":\"something_else <script>\",\"message\":\"x\"}")
        });

        var result = await Resend(handler).SendAsync(FakeKey, Options(), Request("outbox/abc/1"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.DoesNotContain("something_else", result.SafeError);
    }

    [Fact]
    public async Task SendGridAdapter_HasNoIdempotencyMechanism_SoNoKeyHeaderIsSent()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.Accepted));
        var adapter = new SendGridEmailAdapter(new FakeHttpClientFactory(handler), Clock(), NullLogger<SendGridEmailAdapter>.Instance);

        var result = await adapter.SendAsync(FakeKey, Options(), Request("outbox/abc/1"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(handler.LastRequest!.Headers.Contains("Idempotency-Key"));
    }

    [Fact]
    public async Task EmailService_InsideAnOutboxDispatch_StampsTheMessageIdAndAStableKeyOnEachSend()
    {
        var sender = new FakeSender();
        var context = new OutboxDispatchContext();
        var service = new TransactionalEmailService(sender,
            new EmailTemplateRenderer(Microsoft.Extensions.Options.Options.Create(new EmailOptions { AppBaseUrl = "http://localhost:5173" })),
            NullLogger<TransactionalEmailService>.Instance, context);
        var messageId = Guid.NewGuid();

        context.Begin(messageId);
        await service.SendPasswordResetAsync("user@example.com", "tok-1");
        await service.SendAsync("user@example.com", "Second", "<p>2</p>");
        context.End();

        Assert.Equal(2, sender.Requests.Count);
        Assert.All(sender.Requests, r => Assert.Equal(messageId, r.OutboxMessageId));
        Assert.Equal($"outbox/{messageId:N}/1", sender.Requests[0].IdempotencyKey);
        Assert.Equal($"outbox/{messageId:N}/2", sender.Requests[1].IdempotencyKey);
    }

    [Fact]
    public async Task EmailService_OutsideAnOutboxDispatch_SendsWithoutAKey()
    {
        var sender = new FakeSender();
        var service = new TransactionalEmailService(sender,
            new EmailTemplateRenderer(Microsoft.Extensions.Options.Options.Create(new EmailOptions())),
            NullLogger<TransactionalEmailService>.Instance, new OutboxDispatchContext());

        await service.SendAsync("user@example.com", "Subject", "<p>Body</p>");

        var request = Assert.Single(sender.Requests);
        Assert.Null(request.IdempotencyKey);
        Assert.Null(request.OutboxMessageId);
    }
}
