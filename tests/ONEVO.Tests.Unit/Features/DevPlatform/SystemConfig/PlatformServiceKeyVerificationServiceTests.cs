using System.Net;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Helpers;
using ONEVO.Infrastructure.ExternalServices.Email;
using ONEVO.Infrastructure.Services.SystemConfig;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig;

public class PlatformServiceKeyVerificationServiceTests
{
    private const string ResendKey = "re_unit_test_verification_key";
    private const string SendGridKey = "SG.unit_test_verification_key_value";

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public HttpRequestMessage? LastRequest { get; private set; }

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class NamedHttpClientFactory : IHttpClientFactory
    {
        private readonly Dictionary<string, HttpMessageHandler> _handlers;

        public NamedHttpClientFactory(Dictionary<string, HttpMessageHandler> handlers)
            => _handlers = handlers;

        public HttpClient CreateClient(string name)
            => new(_handlers[name], disposeHandler: false);
    }

    private sealed class CapturingLogger : ILogger<PlatformServiceKeyVerificationService>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private static PlatformServiceKeyVerificationService BuildService(
        CapturingHandler resendHandler,
        CapturingHandler sendGridHandler,
        CapturingLogger? logger = null)
    {
        var factory = new NamedHttpClientFactory(new Dictionary<string, HttpMessageHandler>
        {
            [ResendEmailAdapter.HttpClientName] = resendHandler,
            [SendGridEmailAdapter.HttpClientName] = sendGridHandler
        });

        return new PlatformServiceKeyVerificationService(
            factory,
            logger ?? new CapturingLogger());
    }

    [Fact]
    public async Task Resend_LiveVerification_Succeeds_On200()
    {
        var resendHandler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":[{\"id\":\"dom_1\"}]}")
        });
        var service = BuildService(resendHandler, new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await service.VerifyAsync(PlatformServiceKeyCatalog.Resend, ResendKey, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("verified successfully", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpMethod.Get, resendHandler.LastRequest!.Method);
        Assert.Equal("https://api.resend.com/domains", resendHandler.LastRequest.RequestUri!.ToString());
        Assert.Equal("Bearer", resendHandler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal(ResendKey, resendHandler.LastRequest.Headers.Authorization.Parameter);
        Assert.DoesNotContain(ResendKey, result.Message);
    }

    [Fact]
    public async Task Resend_LiveVerification_Fails_On401()
    {
        var resendHandler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"message\":\"invalid api key\"}")
        });
        var logger = new CapturingLogger();
        var service = BuildService(resendHandler, new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)), logger);

        var result = await service.VerifyAsync(PlatformServiceKeyCatalog.Resend, ResendKey, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("401", result.Message);
        Assert.Contains("Unauthorized", result.Message);
        Assert.DoesNotContain(ResendKey, result.Message);
        Assert.DoesNotContain("invalid api key", result.Message);
        Assert.DoesNotContain(ResendKey, string.Join(' ', logger.Messages));
        Assert.DoesNotContain("invalid api key", string.Join(' ', logger.Messages));
    }

    [Fact]
    public async Task SendGrid_LiveVerification_Succeeds_On200()
    {
        var sendGridHandler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"scopes\":[\"mail.send\"]}")
        });
        var service = BuildService(new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)), sendGridHandler);

        var result = await service.VerifyAsync(PlatformServiceKeyCatalog.Sendgrid, SendGridKey, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("verified successfully", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpMethod.Get, sendGridHandler.LastRequest!.Method);
        Assert.Equal("https://api.sendgrid.com/v3/scopes", sendGridHandler.LastRequest.RequestUri!.ToString());
        Assert.Equal(SendGridKey, sendGridHandler.LastRequest.Headers.Authorization!.Parameter);
        Assert.DoesNotContain(SendGridKey, result.Message);
    }

    [Fact]
    public async Task SendGrid_LiveVerification_Fails_On401()
    {
        var sendGridHandler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"errors\":[{\"message\":\"authorization required\"}]}")
        });
        var logger = new CapturingLogger();
        var service = BuildService(new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)), sendGridHandler, logger);

        var result = await service.VerifyAsync(PlatformServiceKeyCatalog.Sendgrid, SendGridKey, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("401", result.Message);
        Assert.DoesNotContain(SendGridKey, result.Message);
        Assert.DoesNotContain("authorization required", result.Message);
        Assert.DoesNotContain(SendGridKey, string.Join(' ', logger.Messages));
        Assert.DoesNotContain("authorization required", string.Join(' ', logger.Messages));
    }

    [Fact]
    public async Task FormatOnlyProviders_ReturnLocalVerificationMessage()
    {
        var service = BuildService(
            new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var cloudflare = await service.VerifyAsync(
            PlatformServiceKeyCatalog.Cloudflare, "cf_token_12345678", CancellationToken.None);
        var r2 = await service.VerifyAsync(
            PlatformServiceKeyCatalog.CloudflareR2, "r2_token_12345678", CancellationToken.None);
        var rekognition = await service.VerifyAsync(
            PlatformServiceKeyCatalog.AwsRekognition, "aws_key_12345678", CancellationToken.None);

        Assert.True(cloudflare.Success);
        Assert.True(r2.Success);
        Assert.True(rekognition.Success);
        Assert.Contains("format-only", cloudflare.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("format-only", r2.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("format-only", rekognition.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not wired", cloudflare.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyKey_IsRejected_WithoutProviderCall()
    {
        var resendHandler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = BuildService(resendHandler, new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await service.VerifyAsync(PlatformServiceKeyCatalog.Resend, "", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("empty", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(resendHandler.LastRequest);
    }
}
