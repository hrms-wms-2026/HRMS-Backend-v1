using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Api.Controllers.Webhooks;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Webhooks.Stripe;
using ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;
using ONEVO.Infrastructure.Services.SystemConfig;
using Stripe;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig;

public sealed class StripeWebhookControllerTests
{
    private const string GatewayKey = "stripe_sandbox";
    private static readonly Guid ConfigIdA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ConfigIdB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const string WebhookSecretA = "whsec_secret_for_config_a_1234567890";
    private const string WebhookSecretB = "whsec_secret_for_config_b_0987654321";
    private const string CorrelationId = "corr-15b-test";

    // 1. ASP.NET Core Routing Infrastructure Disambiguation Tests

    [Fact]
    public async Task Routing_ValidGuid_RoutesToHandleByConfigId()
    {
        var resolver = new Mock<IPaymentGatewayWebhookSecretResolver>();
        resolver
            .Setup(s => s.ResolveByConfigIdAsync("stripe", ConfigIdA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookSecretA);

        using var server = CreateRoutingTestServer(resolver.Object);
        using var client = server.CreateClient();

        var payload = BuildEventPayload();
        var signature = Sign(payload, WebhookSecretA);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        content.Headers.Add("Stripe-Signature", signature);

        var response = await client.PostAsync($"/api/payment-gateways/stripe/{ConfigIdA}/webhook", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        resolver.Verify(
            s => s.ResolveByConfigIdAsync("stripe", ConfigIdA, It.IsAny<CancellationToken>()),
            Times.Once);
        resolver.Verify(
            s => s.ResolveWebhookSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Routing_ValidGatewayKeySlug_RoutesToHandleByGatewayKey()
    {
        var resolver = new Mock<IPaymentGatewayWebhookSecretResolver>();
        resolver
            .Setup(s => s.ResolveWebhookSecretAsync("stripe", GatewayKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookSecretA);

        using var server = CreateRoutingTestServer(resolver.Object);
        using var client = server.CreateClient();

        var payload = BuildEventPayload();
        var signature = Sign(payload, WebhookSecretA);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        content.Headers.Add("Stripe-Signature", signature);

        var response = await client.PostAsync($"/api/payment-gateways/stripe/{GatewayKey}/webhook", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        resolver.Verify(
            s => s.ResolveWebhookSecretAsync("stripe", GatewayKey, It.IsAny<CancellationToken>()),
            Times.Once);
        resolver.Verify(
            s => s.ResolveByConfigIdAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Routing_OldGenericRoute_HasNoEndpointAndReturns404()
    {
        var resolver = new Mock<IPaymentGatewayWebhookSecretResolver>();

        using var server = CreateRoutingTestServer(resolver.Object);
        using var client = server.CreateClient();

        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/stripe/webhook", content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        resolver.Verify(
            s => s.ResolveWebhookSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        resolver.Verify(
            s => s.ResolveByConfigIdAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void AttributeInspection_RouteTemplatesAreCorrect()
    {
        var classRoute = Assert.Single(
            typeof(StripeWebhookController)
                .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
                .Cast<RouteAttribute>());
        Assert.Equal("api/payment-gateways/stripe/{gatewayKey}/webhook", classRoute.Template);

        var configIdMethod = typeof(StripeWebhookController).GetMethod(nameof(StripeWebhookController.HandleByConfigId));
        Assert.NotNull(configIdMethod);
        var postAttr = Assert.Single(
            configIdMethod.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true)
                .Cast<HttpPostAttribute>());
        Assert.Equal("/api/payment-gateways/stripe/{gatewayConfigId:guid}/webhook", postAttr.Template);
    }

    // 2. Controller GatewayConfigId Route Tests

    [Fact]
    public async Task HandleByConfigId_ValidConfigAndSignature_Accepts()
    {
        var resolver = new Mock<IPaymentGatewayWebhookSecretResolver>();
        resolver
            .Setup(s => s.ResolveByConfigIdAsync("stripe", ConfigIdA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookSecretA);

        var payload = BuildEventPayload();
        var signature = Sign(payload, WebhookSecretA);
        var controller = BuildController(resolver.Object, payload, signature);

        var result = await controller.HandleByConfigId(ConfigIdA, CancellationToken.None);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task HandleByConfigId_UnknownConfigId_ReturnsSafeBadRequest()
    {
        var resolver = new Mock<IPaymentGatewayWebhookSecretResolver>();
        resolver
            .Setup(s => s.ResolveByConfigIdAsync("stripe", ConfigIdA, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var controller = BuildController(resolver.Object, "{}", "not-used");

        var result = await controller.HandleByConfigId(ConfigIdA, CancellationToken.None);

        AssertGenericRejection(result, controller);
    }

    // 3. Multi-Config Isolation Tests

    [Fact]
    public async Task MultiConfig_ConfigARouteVerifiesOnlyWithConfigASecret()
    {
        var resolver = BuildMultiConfigResolver();
        var payload = BuildEventPayload();
        var signatureA = Sign(payload, WebhookSecretA);

        var controller = BuildController(resolver.Object, payload, signatureA);
        var result = await controller.HandleByConfigId(ConfigIdA, CancellationToken.None);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task MultiConfig_ConfigBRouteVerifiesOnlyWithConfigBSecret()
    {
        var resolver = BuildMultiConfigResolver();
        var payload = BuildEventPayload();
        var signatureB = Sign(payload, WebhookSecretB);

        var controller = BuildController(resolver.Object, payload, signatureB);
        var result = await controller.HandleByConfigId(ConfigIdB, CancellationToken.None);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task MultiConfig_ConfigARouteRejectsConfigBSignature()
    {
        var resolver = BuildMultiConfigResolver();
        var payload = BuildEventPayload();
        var signatureB = Sign(payload, WebhookSecretB);

        var controller = BuildController(resolver.Object, payload, signatureB);
        var result = await controller.HandleByConfigId(ConfigIdA, CancellationToken.None);

        AssertGenericRejection(result, controller);
    }

    // 4. Resolver Logic Tests

    [Fact]
    public async Task Resolver_ResolveByConfigIdAsync_ValidConfig_ReturnsSecret()
    {
        var repo = new Mock<IPaymentGatewayRepository>();
        var enc = new Mock<IEncryptionService>();

        repo.Setup(r => r.GetByIdAsync(ConfigIdA, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayConfig
            {
                Id = ConfigIdA,
                Provider = "stripe",
                IsActive = true
            });

        repo.Setup(r => r.GetActiveCredentialAsync(ConfigIdA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayCredential
            {
                PaymentGatewayConfigId = ConfigIdA,
                IsActive = true,
                WebhookSecretEncrypted = Encoding.UTF8.GetBytes("encrypted-bytes")
            });

        enc.Setup(e => e.DecryptBytes(It.IsAny<byte[]>()))
            .Returns(WebhookSecretA);

        var resolver = new PaymentGatewayWebhookSecretResolver(repo.Object, enc.Object);
        var resolvedSecret = await resolver.ResolveByConfigIdAsync("stripe", ConfigIdA, CancellationToken.None);

        Assert.Equal(WebhookSecretA, resolvedSecret);
    }

    [Fact]
    public async Task Resolver_ResolveByConfigIdAsync_InactiveConfig_ReturnsNull()
    {
        var repo = new Mock<IPaymentGatewayRepository>();
        var enc = new Mock<IEncryptionService>();

        repo.Setup(r => r.GetByIdAsync(ConfigIdA, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayConfig
            {
                Id = ConfigIdA,
                Provider = "stripe",
                IsActive = false
            });

        var resolver = new PaymentGatewayWebhookSecretResolver(repo.Object, enc.Object);
        var resolvedSecret = await resolver.ResolveByConfigIdAsync("stripe", ConfigIdA, CancellationToken.None);

        Assert.Null(resolvedSecret);
    }

    [Fact]
    public async Task Resolver_ResolveByConfigIdAsync_WrongProvider_ReturnsNull()
    {
        var repo = new Mock<IPaymentGatewayRepository>();
        var enc = new Mock<IEncryptionService>();

        repo.Setup(r => r.GetByIdAsync(ConfigIdA, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayConfig
            {
                Id = ConfigIdA,
                Provider = "paddle",
                IsActive = true
            });

        var resolver = new PaymentGatewayWebhookSecretResolver(repo.Object, enc.Object);
        var resolvedSecret = await resolver.ResolveByConfigIdAsync("stripe", ConfigIdA, CancellationToken.None);

        Assert.Null(resolvedSecret);
    }

    [Fact]
    public async Task Resolver_ResolveByConfigIdAsync_MissingCredential_ReturnsNull()
    {
        var repo = new Mock<IPaymentGatewayRepository>();
        var enc = new Mock<IEncryptionService>();

        repo.Setup(r => r.GetByIdAsync(ConfigIdA, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayConfig
            {
                Id = ConfigIdA,
                Provider = "stripe",
                IsActive = true
            });

        repo.Setup(r => r.GetActiveCredentialAsync(ConfigIdA, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentGatewayCredential?)null);

        var resolver = new PaymentGatewayWebhookSecretResolver(repo.Object, enc.Object);
        var resolvedSecret = await resolver.ResolveByConfigIdAsync("stripe", ConfigIdA, CancellationToken.None);

        Assert.Null(resolvedSecret);
    }

    [Fact]
    public async Task Resolver_ResolveByConfigIdAsync_MissingWebhookSecret_ReturnsNull()
    {
        var repo = new Mock<IPaymentGatewayRepository>();
        var enc = new Mock<IEncryptionService>();

        repo.Setup(r => r.GetByIdAsync(ConfigIdA, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayConfig
            {
                Id = ConfigIdA,
                Provider = "stripe",
                IsActive = true
            });

        repo.Setup(r => r.GetActiveCredentialAsync(ConfigIdA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayCredential
            {
                PaymentGatewayConfigId = ConfigIdA,
                IsActive = true,
                WebhookSecretEncrypted = null
            });

        var resolver = new PaymentGatewayWebhookSecretResolver(repo.Object, enc.Object);
        var resolvedSecret = await resolver.ResolveByConfigIdAsync("stripe", ConfigIdA, CancellationToken.None);

        Assert.Null(resolvedSecret);
    }

    // 5. Security Guard Tests

    [Fact]
    public async Task Security_NoSecretsExposedInResponses()
    {
        var resolver = new Mock<IPaymentGatewayWebhookSecretResolver>();
        resolver
            .Setup(s => s.ResolveByConfigIdAsync("stripe", ConfigIdA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookSecretA);

        var payload = BuildEventPayload();
        var controller = BuildController(resolver.Object, payload, "t=1,v1=invalid_sig");

        var result = await controller.HandleByConfigId(ConfigIdA, CancellationToken.None);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);

        var responseString = JsonSerializer.Serialize(badRequest.Value);
        Assert.DoesNotContain(WebhookSecretA, responseString);
        Assert.DoesNotContain("secret", responseString, StringComparison.OrdinalIgnoreCase);
    }

    // 6. Signature first, one generic rejection (task 15 follow-up)

    [Fact]
    public async Task MissingSignature_Returns400_BeforeSecretLookupOrDispatch()
    {
        var resolver = new Mock<IPaymentGatewayWebhookSecretResolver>(MockBehavior.Strict);
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var controller = BuildController(resolver.Object, CheckoutCompletedPayload(), signature: null, mediator.Object);

        var result = await controller.HandleByConfigId(ConfigIdA, CancellationToken.None);

        AssertGenericRejection(result, controller);
        resolver.VerifyNoOtherCalls();
        mediator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BlankSignature_GatewayKeyRoute_Returns400_BeforeSecretLookupOrDispatch()
    {
        var resolver = new Mock<IPaymentGatewayWebhookSecretResolver>(MockBehavior.Strict);
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var controller = BuildController(resolver.Object, CheckoutCompletedPayload(), signature: "  ", mediator.Object);

        var result = await controller.Handle(GatewayKey, CancellationToken.None);

        AssertGenericRejection(result, controller);
        resolver.VerifyNoOtherCalls();
        mediator.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("t=1,v1=0000")]
    [InlineData("garbage")]
    public async Task BadSignature_OnAHandledEventType_Returns400_AndDispatchesNothing(string signature)
    {
        var resolver = BuildMultiConfigResolver();
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var controller = BuildController(resolver.Object, CheckoutCompletedPayload(), signature, mediator.Object);

        var result = await controller.HandleByConfigId(ConfigIdA, CancellationToken.None);

        AssertGenericRejection(result, controller);
        mediator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SignatureForADifferentBody_Returns400_AndDispatchesNothing()
    {
        var resolver = BuildMultiConfigResolver();
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var signed = CheckoutCompletedPayload();
        var tampered = signed.Replace("cus_test_1", "cus_attacker");
        var controller = BuildController(resolver.Object, tampered, Sign(signed, WebhookSecretA), mediator.Object);

        var result = await controller.HandleByConfigId(ConfigIdA, CancellationToken.None);

        AssertGenericRejection(result, controller);
        mediator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ValidSignature_OnAHandledEventType_DispatchesOnce()
    {
        var resolver = BuildMultiConfigResolver();
        var mediator = new Mock<IMediator>();
        var payload = CheckoutCompletedPayload();
        var controller = BuildController(resolver.Object, payload, Sign(payload, WebhookSecretA), mediator.Object);

        var result = await controller.HandleByConfigId(ConfigIdA, CancellationToken.None);

        Assert.IsType<OkResult>(result);
        mediator.Verify(
            m => m.Send(
                It.Is<ProcessStripeEventCommand>(c => c.EventType == "checkout.session.completed" && c.StripeCustomerId == "cus_test_1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EveryRejection_HasTheSameBody_AndLogsTheReasonServerSide()
    {
        var logger = new CapturingLogger();
        var unconfigured = new Mock<IPaymentGatewayWebhookSecretResolver>();
        var configured = BuildMultiConfigResolver();
        var payload = CheckoutCompletedPayload();

        var cases = new (Func<Task<IActionResult>> Run, string Reason)[]
        {
            (() => BuildController(unconfigured.Object, payload, "sig", logger: logger).Handle("Bad Key!", CancellationToken.None), "invalid_gateway_key"),
            (() => BuildController(unconfigured.Object, payload, null, logger: logger).HandleByConfigId(ConfigIdA, CancellationToken.None), "missing_signature"),
            (() => BuildController(unconfigured.Object, payload, "sig", logger: logger).HandleByConfigId(ConfigIdA, CancellationToken.None), "not_configured"),
            (() => BuildController(configured.Object, payload, Sign(payload, WebhookSecretB), logger: logger).HandleByConfigId(ConfigIdA, CancellationToken.None), "invalid_signature"),
        };

        var bodies = new List<string>();
        foreach (var (run, reason) in cases)
        {
            var result = await run();
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            bodies.Add(JsonSerializer.Serialize(badRequest.Value));
            Assert.Contains(logger.Messages, m => m.Contains(reason) && m.Contains(CorrelationId));
        }

        Assert.Single(bodies.Distinct());
        Assert.DoesNotContain("configured", bodies[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signature", bodies[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Stripe", bodies[0], StringComparison.Ordinal);
        Assert.Contains(CorrelationId, bodies[0]);
    }

    private static void AssertGenericRejection(IActionResult result, ControllerBase controller)
    {
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var rejection = Assert.IsType<WebhookRejection>(badRequest.Value);
        Assert.Equal(400, rejection.Status);
        Assert.Equal(WebhookRejection.GenericDetail, rejection.Detail);
        Assert.Equal(CorrelationId, rejection.CorrelationId);
        Assert.Equal(CorrelationId, controller.HttpContext.Items["X-Correlation-Id"]);
    }

    private static string CheckoutCompletedPayload()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $$"""
            {
              "id": "evt_checkout_test",
              "object": "event",
              "api_version": "{{StripeConfiguration.ApiVersion}}",
              "created": {{now}},
              "data": { "object": { "id": "cs_test_1", "object": "checkout.session", "customer": "cus_test_1", "subscription": "sub_test_1", "metadata": {} } },
              "livemode": false,
              "pending_webhooks": 1,
              "request": { "id": null, "idempotency_key": null },
              "type": "checkout.session.completed"
            }
            """;
    }

    private sealed class CapturingLogger : ILogger<StripeWebhookController>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    // Helper Methods

    private static TestServer CreateRoutingTestServer(IPaymentGatewayWebhookSecretResolver resolver)
    {
#pragma warning disable ASPDEPR004, ASPDEPR008
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddControllers()
                    .AddApplicationPart(typeof(StripeWebhookController).Assembly);
                services.AddSingleton(Mock.Of<IMediator>());
                services.AddSingleton(resolver);
                services.AddLogging();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });
            });

        return new TestServer(builder);
#pragma warning restore ASPDEPR004, ASPDEPR008
    }

    private static Mock<IPaymentGatewayWebhookSecretResolver> BuildMultiConfigResolver()
    {
        var resolver = new Mock<IPaymentGatewayWebhookSecretResolver>();
        resolver
            .Setup(s => s.ResolveByConfigIdAsync("stripe", ConfigIdA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookSecretA);
        resolver
            .Setup(s => s.ResolveByConfigIdAsync("stripe", ConfigIdB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookSecretB);
        return resolver;
    }

    private static StripeWebhookController BuildController(
        IPaymentGatewayWebhookSecretResolver resolver,
        string payload,
        string? signature,
        IMediator? mediator = null,
        ILogger<StripeWebhookController>? logger = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        if (signature is not null)
        {
            httpContext.Request.Headers["Stripe-Signature"] = signature;
        }
        httpContext.Items["X-Correlation-Id"] = CorrelationId;

        return new StripeWebhookController(
            mediator ?? Mock.Of<IMediator>(),
            resolver,
            logger ?? NullLogger<StripeWebhookController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static string BuildEventPayload()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $$"""
            {
              "id": "evt_gateway_key_routing_test",
              "object": "event",
              "api_version": "{{StripeConfiguration.ApiVersion}}",
              "created": {{now}},
              "data": { "object": { "object": "test_object" } },
              "livemode": false,
              "pending_webhooks": 1,
              "request": { "id": null, "idempotency_key": null },
              "type": "test.gateway_key_routing"
            }
            """;
    }

    private static string Sign(string payload, string secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signedPayload = $"{timestamp}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload)))
            .ToLowerInvariant();
        return $"t={timestamp},v1={hash}";
    }
}
