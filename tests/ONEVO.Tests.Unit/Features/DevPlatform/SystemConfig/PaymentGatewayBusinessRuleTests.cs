using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.CreatePaymentGateway;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.RotatePaymentGatewayCredential;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig;

public sealed class PaymentGatewayBusinessRuleTests
{
    [Fact]
    public async Task Create_CreatesOneActiveCredentialAndManyNormalizedCountryRoutes()
    {
        var credentials = new List<PaymentGatewayCredential>();
        var routes = new List<PaymentGatewayCountryRoute>();
        var repository = CreateRepository();
        repository
            .Setup(repo => repo.AddCredentialAsync(
                It.IsAny<PaymentGatewayCredential>(),
                It.IsAny<CancellationToken>()))
            .Callback<PaymentGatewayCredential, CancellationToken>(
                (credential, _) => credentials.Add(credential))
            .Returns(Task.CompletedTask);
        repository
            .Setup(repo => repo.AddCountryRouteAsync(
                It.IsAny<PaymentGatewayCountryRoute>(),
                It.IsAny<CancellationToken>()))
            .Callback<PaymentGatewayCountryRoute, CancellationToken>(
                (route, _) => routes.Add(route))
            .Returns(Task.CompletedTask);
        var handler = new CreatePaymentGatewayCommandHandler(
            repository.Object,
            EncryptionService());

        var result = await handler.Handle(
            CreateCommand("sandbox", ["lk", "gb"]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var credential = Assert.Single(credentials);
        Assert.True(credential.IsActive);
        Assert.Equal(1, credential.CredentialVersion);
        Assert.Equal(["LK", "GB"], routes.Select(route => route.CountryCode));
        Assert.Single(routes.Select(route => route.GatewayConfigId).Distinct());
        Assert.All(routes, route => Assert.Equal("sandbox", route.Environment));
        repository.Verify(
            repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Create_DuplicateNormalizedCountryCode_CannotCreateTwoActiveRoutes()
    {
        var repository = CreateRepository();
        var handler = new CreatePaymentGatewayCommandHandler(
            repository.Object,
            EncryptionService());

        var result = await handler.Handle(
            CreateCommand("production", ["LK", "lk"]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        repository.Verify(
            repo => repo.AddCountryRouteAsync(
                It.IsAny<PaymentGatewayCountryRoute>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        repository.Verify(
            repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("1K")]
    [InlineData("L-")]
    [InlineData("L")]
    [InlineData("LKA")]
    public async Task Create_NonIsoAlpha2CountryCode_IsRejected(string countryCode)
    {
        var repository = CreateRepository();
        var handler = new CreatePaymentGatewayCommandHandler(
            repository.Object,
            EncryptionService());

        var result = await handler.Handle(
            CreateCommand("production", [countryCode]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        repository.Verify(
            repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_ProductionConflict_DoesNotBlockIndependentSandboxRoute()
    {
        var repository = CreateRepository();
        repository
            .Setup(repo => repo.HasConflictingCountryRouteAsync(
                "LK",
                It.IsAny<string>(),
                Guid.Empty,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                string _,
                string environment,
                Guid _,
                CancellationToken _) => environment == "production");
        var handler = new CreatePaymentGatewayCommandHandler(
            repository.Object,
            EncryptionService());

        var result = await handler.Handle(
            CreateCommand("sandbox", ["LK"]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        repository.Verify(
            repo => repo.HasConflictingCountryRouteAsync(
                "LK",
                "sandbox",
                Guid.Empty,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Rotate_RepeatedlyLeavesOneActiveCredentialAndMonotonicVersions()
    {
        var gatewayId = Guid.NewGuid();
        var credentials = new List<PaymentGatewayCredential>
        {
            new()
            {
                Id = Guid.NewGuid(),
                PaymentGatewayConfigId = gatewayId,
                CredentialVersion = 1,
                IsActive = true
            }
        };
        var repository = RotatingRepository(gatewayId, credentials);
        var handler = new RotatePaymentGatewayCredentialCommandHandler(
            repository.Object,
            EncryptionService());

        var first = await handler.Handle(
            RotateCommand(gatewayId),
            CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Single(credentials, credential => credential.IsActive);
        Assert.Equal(2, credentials.Single(credential => credential.IsActive).CredentialVersion);

        var second = await handler.Handle(
            RotateCommand(gatewayId),
            CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Single(credentials, credential => credential.IsActive);
        Assert.Equal(3, credentials.Single(credential => credential.IsActive).CredentialVersion);
        Assert.Equal([1, 2, 3], credentials.Select(credential => credential.CredentialVersion));
        Assert.All(
            credentials.Where(credential => !credential.IsActive),
            credential =>
            {
                Assert.NotNull(credential.DeactivatedAt);
                Assert.NotNull(credential.DeactivatedById);
            });
        repository.Verify(
            repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Rotate_RepairsMultipleActiveCredentialsBeforeAddingNewVersion()
    {
        var gatewayId = Guid.NewGuid();
        var credentials = new List<PaymentGatewayCredential>
        {
            new() { PaymentGatewayConfigId = gatewayId, CredentialVersion = 1, IsActive = true },
            new() { PaymentGatewayConfigId = gatewayId, CredentialVersion = 2, IsActive = true }
        };
        var repository = RotatingRepository(gatewayId, credentials);
        var handler = new RotatePaymentGatewayCredentialCommandHandler(
            repository.Object,
            EncryptionService());

        var result = await handler.Handle(
            RotateCommand(gatewayId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var active = Assert.Single(credentials, credential => credential.IsActive);
        Assert.Equal(3, active.CredentialVersion);
    }

    [Fact]
    public void ResponseDtos_ExposeNoSecretOrEncryptedCredentialFields()
    {
        var responseTypes = new[]
        {
            typeof(PaymentGatewayConfigDto),
            typeof(PaymentGatewayCountryRouteDto),
            typeof(ResolvedGatewayDto)
        };

        foreach (var responseType in responseTypes)
        {
            var names = responseType
                .GetProperties()
                .Select(property => property.Name)
                .ToArray();
            Assert.DoesNotContain(
                names,
                name =>
                    name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Encrypted", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static Mock<IPaymentGatewayRepository> CreateRepository()
    {
        var repository = new Mock<IPaymentGatewayRepository>();
        repository
            .Setup(repo => repo.GetByGatewayKeyAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentGatewayConfig?)null);
        repository
            .Setup(repo => repo.HasConflictingCountryRouteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repository
            .Setup(repo => repo.AddAsync(
                It.IsAny<PaymentGatewayConfig>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repository
            .Setup(repo => repo.AddCredentialAsync(
                It.IsAny<PaymentGatewayCredential>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repository
            .Setup(repo => repo.AddCountryRouteAsync(
                It.IsAny<PaymentGatewayCountryRoute>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repository
            .Setup(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return repository;
    }

    private static Mock<IPaymentGatewayRepository> RotatingRepository(
        Guid gatewayId,
        List<PaymentGatewayCredential> credentials)
    {
        var repository = new Mock<IPaymentGatewayRepository>();
        repository
            .Setup(repo => repo.GetByIdAsync(
                gatewayId,
                false,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayConfig { Id = gatewayId });
        repository
            .Setup(repo => repo.GetActiveCredentialsAsync(
                gatewayId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
                (IReadOnlyList<PaymentGatewayCredential>)credentials
                    .Where(credential => credential.IsActive)
                    .ToList());
        repository
            .Setup(repo => repo.GetMaxCredentialVersionAsync(
                gatewayId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => credentials.Max(credential => credential.CredentialVersion));
        repository
            .Setup(repo => repo.AddCredentialAsync(
                It.IsAny<PaymentGatewayCredential>(),
                It.IsAny<CancellationToken>()))
            .Callback<PaymentGatewayCredential, CancellationToken>(
                (credential, _) => credentials.Add(credential))
            .Returns(Task.CompletedTask);
        repository
            .Setup(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return repository;
    }

    private static IEncryptionService EncryptionService()
    {
        var encryption = new Mock<IEncryptionService>();
        encryption
            .Setup(service => service.EncryptBytes(It.IsAny<string>()))
            .Returns<string>(value => [.. System.Text.Encoding.UTF8.GetBytes(value)]);
        return encryption.Object;
    }

    private static CreatePaymentGatewayCommand CreateCommand(
        string environment,
        IReadOnlyList<string> countryCodes) =>
        new(
            $"stripe-{environment}-{Guid.NewGuid():N}",
            "stripe",
            environment,
            "Stripe",
            null,
            null,
            null,
            null,
            true,
            "provider-secret",
            "webhook-secret",
            countryCodes,
            countryCodes.Select<string, string?>(_ => null).ToArray(),
            Guid.NewGuid());

    private static RotatePaymentGatewayCredentialCommand RotateCommand(Guid gatewayId) =>
        new(
            gatewayId,
            "rotated-provider-secret",
            "rotated-webhook-secret",
            Guid.NewGuid());
}
