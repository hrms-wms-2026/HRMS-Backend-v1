using System.Net;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Helpers;
using ONEVO.Infrastructure.ExternalServices.Email;
using ONEVO.Infrastructure.ExternalServices.Storage.CloudflareR2;
using ONEVO.Infrastructure.Services.Monitoring.Biometrics;
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

    private sealed class FakeRekognitionProbe : IAwsRekognitionConnectionProbe
    {
        public AwsRekognitionProbeResult Result { get; set; } =
            new(true, "Connected to Amazon Rekognition.", "onevo-rekognition", "eu-west-2");

        public string? LastAccessKeyId { get; private set; }
        public string? LastRegion { get; private set; }

        public Task<AwsRekognitionProbeResult> ProbeAsync(
            string accessKeyId, string secretAccessKey, string region, CancellationToken ct)
        {
            LastAccessKeyId = accessKeyId;
            LastRegion = region;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeR2Probe : ICloudflareR2ConnectionProbe
    {
        public CloudflareR2ProbeResult Result { get; set; } =
            new(true, "Cloudflare R2 bucket verified.", "screenshots", "auto");

        public string? LastBucket { get; private set; }
        public string? LastEndpoint { get; private set; }

        public Task<CloudflareR2ProbeResult> ProbeAsync(
            string accessKeyId,
            string secretAccessKey,
            string bucketName,
            string endpoint,
            string region,
            CancellationToken ct)
        {
            LastBucket = bucketName;
            LastEndpoint = endpoint;
            return Task.FromResult(Result);
        }
    }

    private static PlatformServiceKeyVerificationService BuildService(
        CapturingHandler resendHandler,
        CapturingHandler sendGridHandler,
        CapturingLogger? logger = null,
        IAwsRekognitionConnectionProbe? rekognitionProbe = null,
        ICloudflareR2ConnectionProbe? r2Probe = null)
    {
        var factory = new NamedHttpClientFactory(new Dictionary<string, HttpMessageHandler>
        {
            [ResendEmailAdapter.HttpClientName] = resendHandler,
            [SendGridEmailAdapter.HttpClientName] = sendGridHandler
        });

        return new PlatformServiceKeyVerificationService(
            factory,
            rekognitionProbe ?? new FakeRekognitionProbe(),
            r2Probe ?? new FakeR2Probe(),
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

        Assert.True(cloudflare.Success);
        Assert.Contains("format-only", cloudflare.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not wired", cloudflare.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CloudflareR2_ValidBundle_ProbesTheBucket()
    {
        var probe = new FakeR2Probe();
        var service = BuildService(
            new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            r2Probe: probe);

        var result = await service.VerifyAsync(
            PlatformServiceKeyCatalog.CloudflareR2,
            "{\"accountId\":\"acct\",\"bucketName\":\"screenshots\",\"accessKeyId\":\"r2-key\",\"secretAccessKey\":\"r2-secret\",\"endpoint\":\"https://acct.r2.cloudflarestorage.com\"}",
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("screenshots", probe.LastBucket);
        Assert.Equal("https://acct.r2.cloudflarestorage.com", probe.LastEndpoint);
        Assert.Equal("Cloudflare R2", result.Service);
        Assert.Equal("screenshots", result.Identity);
        Assert.DoesNotContain("r2-secret", result.Message);
        Assert.DoesNotContain("r2-key", result.Message);
    }

    [Fact]
    public async Task CloudflareR2_RejectedCredentials_FailWithoutSecrets()
    {
        var probe = new FakeR2Probe
        {
            Result = new CloudflareR2ProbeResult(false, "Cloudflare R2 rejected the credentials. Check the access key, secret, and bucket.", "screenshots", "auto")
        };
        var service = BuildService(
            new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            r2Probe: probe);

        var result = await service.VerifyAsync(
            PlatformServiceKeyCatalog.CloudflareR2,
            "{\"accountId\":\"acct\",\"bucketName\":\"screenshots\",\"accessKeyId\":\"r2-key\",\"secretAccessKey\":\"r2-secret\",\"endpoint\":\"https://acct.r2.cloudflarestorage.com\"}",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("rejected", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("r2-secret", result.Message);
    }

    [Fact]
    public async Task AwsRekognition_ValidJsonBundle_ProbesAwsAndReturnsIdentity()
    {
        var probe = new FakeRekognitionProbe();
        var service = BuildService(
            new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            rekognitionProbe: probe);

        var result = await service.VerifyAsync(
            PlatformServiceKeyCatalog.AwsRekognition,
            """{"accessKeyId":"AKIAEXAMPLEKEY0001","secretAccessKey":"secret-access-key-value","region":"eu-west-2"}""",
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("onevo-rekognition", result.Identity);
        Assert.Equal("eu-west-2", result.Region);
        Assert.Equal("Amazon Rekognition", result.Service);
        Assert.Equal("AKIAEXAMPLEKEY0001", probe.LastAccessKeyId);
        Assert.DoesNotContain("AKIAEXAMPLEKEY0001", result.Message);
        Assert.DoesNotContain("secret-access-key-value", result.Message);
    }

    [Fact]
    public async Task AwsRekognition_OpaqueString_Fails()
    {
        var service = BuildService(
            new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await service.VerifyAsync(
            PlatformServiceKeyCatalog.AwsRekognition, "aws_key_12345678", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Access Key ID", result.Message, StringComparison.OrdinalIgnoreCase);
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
