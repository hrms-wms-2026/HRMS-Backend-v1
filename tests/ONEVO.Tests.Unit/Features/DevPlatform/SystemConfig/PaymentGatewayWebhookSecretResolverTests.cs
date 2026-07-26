using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;
using ONEVO.Infrastructure.Services.SystemConfig;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig;

public sealed class PaymentGatewayWebhookSecretResolverTests
{
    [Fact]
    public async Task Resolve_MultipleActiveStripeConfigs_SelectsExactGatewayKey()
    {
        var sandboxId = Guid.NewGuid();
        var sandboxSecret = new byte[] { 1, 2, 3 };
        var repository = new Mock<IPaymentGatewayRepository>();
        var encryption = new Mock<IEncryptionService>();
        repository
            .Setup(repo => repo.GetByGatewayKeyAsync(
                "stripe_sandbox",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayConfig
            {
                Id = sandboxId,
                GatewayKey = "stripe_sandbox",
                Provider = "stripe",
                Environment = "sandbox",
                IsActive = true
            });
        repository
            .Setup(repo => repo.GetActiveCredentialAsync(
                sandboxId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayCredential
            {
                PaymentGatewayConfigId = sandboxId,
                IsActive = true,
                WebhookSecretEncrypted = sandboxSecret
            });
        encryption
            .Setup(service => service.DecryptBytes(sandboxSecret))
            .Returns("sandbox-database-webhook-secret");

        var resolver = new PaymentGatewayWebhookSecretResolver(
            repository.Object,
            encryption.Object);

        var result = await resolver.ResolveWebhookSecretAsync(
            "stripe",
            "stripe_sandbox",
            CancellationToken.None);

        Assert.Equal("sandbox-database-webhook-secret", result);
        repository.Verify(
            repo => repo.GetByGatewayKeyAsync(
                "stripe_sandbox",
                It.IsAny<CancellationToken>()),
            Times.Once);
        repository.Verify(
            repo => repo.ListAllAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Resolve_UnknownGatewayKey_FailsClosed()
    {
        var repository = new Mock<IPaymentGatewayRepository>();
        repository
            .Setup(repo => repo.GetByGatewayKeyAsync(
                "stripe_unknown",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentGatewayConfig?)null);
        var encryption = new Mock<IEncryptionService>();
        var resolver = new PaymentGatewayWebhookSecretResolver(
            repository.Object,
            encryption.Object);

        var result = await resolver.ResolveWebhookSecretAsync(
            "stripe",
            "stripe_unknown",
            CancellationToken.None);

        Assert.Null(result);
        encryption.Verify(
            service => service.DecryptBytes(It.IsAny<byte[]>()),
            Times.Never);
    }

    [Theory]
    [InlineData("stripe", false)]
    [InlineData("payhere", true)]
    public async Task Resolve_InactiveOrProviderMismatchedConfig_FailsClosed(
        string storedProvider,
        bool isActive)
    {
        var configId = Guid.NewGuid();
        var repository = new Mock<IPaymentGatewayRepository>();
        repository
            .Setup(repo => repo.GetByGatewayKeyAsync(
                "stripe_live",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayConfig
            {
                Id = configId,
                GatewayKey = "stripe_live",
                Provider = storedProvider,
                Environment = "production",
                IsActive = isActive
            });
        var resolver = new PaymentGatewayWebhookSecretResolver(
            repository.Object,
            Mock.Of<IEncryptionService>());

        var result = await resolver.ResolveWebhookSecretAsync(
            "stripe",
            "stripe_live",
            CancellationToken.None);

        Assert.Null(result);
        repository.Verify(
            repo => repo.GetActiveCredentialAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Resolve_MissingActiveCredential_FailsClosed()
    {
        var configId = Guid.NewGuid();
        var repository = RepositoryReturningActiveStripe(
            configId,
            "stripe_live");
        repository
            .Setup(repo => repo.GetActiveCredentialAsync(
                configId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentGatewayCredential?)null);
        var encryption = new Mock<IEncryptionService>();
        var resolver = new PaymentGatewayWebhookSecretResolver(
            repository.Object,
            encryption.Object);

        var result = await resolver.ResolveWebhookSecretAsync(
            "stripe",
            "stripe_live",
            CancellationToken.None);

        Assert.Null(result);
        encryption.Verify(
            service => service.DecryptBytes(It.IsAny<byte[]>()),
            Times.Never);
    }

    [Fact]
    public async Task Resolve_MissingWebhookSecret_FailsClosed()
    {
        var configId = Guid.NewGuid();
        var repository = RepositoryReturningActiveStripe(
            configId,
            "stripe_live");
        repository
            .Setup(repo => repo.GetActiveCredentialAsync(
                configId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayCredential
            {
                PaymentGatewayConfigId = configId,
                IsActive = true,
                WebhookSecretEncrypted = null
            });
        var encryption = new Mock<IEncryptionService>();
        var resolver = new PaymentGatewayWebhookSecretResolver(
            repository.Object,
            encryption.Object);

        var result = await resolver.ResolveWebhookSecretAsync(
            "stripe",
            "stripe_live",
            CancellationToken.None);

        Assert.Null(result);
        encryption.Verify(
            service => service.DecryptBytes(It.IsAny<byte[]>()),
            Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Stripe_Live")]
    [InlineData("stripe live")]
    [InlineData("../stripe_live")]
    public async Task Resolve_MalformedGatewayKey_FailsBeforeRepositoryLookup(
        string gatewayKey)
    {
        var repository = new Mock<IPaymentGatewayRepository>();
        var resolver = new PaymentGatewayWebhookSecretResolver(
            repository.Object,
            Mock.Of<IEncryptionService>());

        var result = await resolver.ResolveWebhookSecretAsync(
            "stripe",
            gatewayKey,
            CancellationToken.None);

        Assert.Null(result);
        repository.Verify(
            repo => repo.GetByGatewayKeyAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static Mock<IPaymentGatewayRepository> RepositoryReturningActiveStripe(
        Guid configId,
        string gatewayKey)
    {
        var repository = new Mock<IPaymentGatewayRepository>();
        repository
            .Setup(repo => repo.GetByGatewayKeyAsync(
                gatewayKey,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayConfig
            {
                Id = configId,
                GatewayKey = gatewayKey,
                Provider = "stripe",
                Environment = "production",
                IsActive = true
            });
        return repository;
    }
}
