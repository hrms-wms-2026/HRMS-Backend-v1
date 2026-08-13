using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.CreatePaymentGateway;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.RotatePaymentGatewayCredential;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.UpdatePaymentGatewayMetadata;
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
    public async Task Update_UpdatesMetadataAndReplacesCountryRoutesWithoutTouchingCredentials()
    {
        var gatewayId = Guid.NewGuid();
        var existingRoute = new PaymentGatewayCountryRoute
        {
            Id = Guid.NewGuid(),
            GatewayConfigId = gatewayId,
            CountryCode = "LK",
            Environment = "production",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var config = new PaymentGatewayConfig
        {
            Id = gatewayId,
            GatewayKey = "stripe-prod",
            Provider = "stripe",
            Environment = "production",
            DisplayName = "Stripe Production",
            IsActive = true,
            CountryRoutes = [existingRoute]
        };
        var addedRoutes = new List<PaymentGatewayCountryRoute>();
        var repository = UpdateRepository(gatewayId, config, addedRoutes);
        var handler = new UpdatePaymentGatewayMetadataCommandHandler(repository.Object);

        var result = await handler.Handle(
            new UpdatePaymentGatewayMetadataCommand(
                gatewayId,
                "Stripe Global Production",
                "https://example.com/logo.png",
                "pk_live_123",
                "merchant-1",
                "https://example.com/webhook",
                false,
                ["GB"],
                ["United Kingdom"],
                Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Stripe Global Production", config.DisplayName);
        Assert.Equal("https://example.com/logo.png", config.LogoUrl);
        Assert.Equal("pk_live_123", config.PublicKey);
        Assert.Equal("merchant-1", config.MerchantId);
        Assert.Equal("https://example.com/webhook", config.WebhookUrl);
        Assert.False(config.IsActive);
        Assert.False(existingRoute.IsActive);
        var newRoute = Assert.Single(addedRoutes);
        Assert.Equal("GB", newRoute.CountryCode);
        Assert.Equal("United Kingdom", newRoute.CountryNameSnapshot);
        Assert.True(newRoute.IsActive);
        repository.Verify(
            repo => repo.AddCredentialAsync(
                It.IsAny<PaymentGatewayCredential>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        repository.Verify(
            repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Update_ReactivatesExistingRouteWhenCountryIsReassigned()
    {
        var gatewayId = Guid.NewGuid();
        var inactiveRoute = new PaymentGatewayCountryRoute
        {
            Id = Guid.NewGuid(),
            GatewayConfigId = gatewayId,
            CountryCode = "GB",
            Environment = "sandbox",
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var config = new PaymentGatewayConfig
        {
            Id = gatewayId,
            GatewayKey = "stripe-sandbox",
            Provider = "stripe",
            Environment = "sandbox",
            DisplayName = "Stripe Sandbox",
            IsActive = true,
            CountryRoutes = [inactiveRoute]
        };
        var repository = UpdateRepository(gatewayId, config, []);
        var handler = new UpdatePaymentGatewayMetadataCommandHandler(repository.Object);

        var result = await handler.Handle(
            new UpdatePaymentGatewayMetadataCommand(
                gatewayId,
                null,
                null,
                null,
                null,
                null,
                null,
                ["GB"],
                ["United Kingdom"],
                Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(inactiveRoute.IsActive);
        Assert.Equal("United Kingdom", inactiveRoute.CountryNameSnapshot);
        repository.Verify(
            repo => repo.AddCountryRouteAsync(
                It.IsAny<PaymentGatewayCountryRoute>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Update_EmptyCountryCodes_Returns400_AndDoesNotDeactivateExistingRoutes()
    {
        var gatewayId = Guid.NewGuid();
        var existingRoute = new PaymentGatewayCountryRoute
        {
            Id = Guid.NewGuid(),
            GatewayConfigId = gatewayId,
            CountryCode = "LK",
            Environment = "production",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var config = new PaymentGatewayConfig
        {
            Id = gatewayId,
            GatewayKey = "stripe-prod",
            Provider = "stripe",
            Environment = "production",
            DisplayName = "Stripe Production",
            IsActive = true,
            CountryRoutes = [existingRoute]
        };
        var repository = UpdateRepository(gatewayId, config, []);
        var handler = new UpdatePaymentGatewayMetadataCommandHandler(repository.Object);

        var result = await handler.Handle(
            new UpdatePaymentGatewayMetadataCommand(
                gatewayId,
                "Stripe Production Updated",
                null,
                null,
                null,
                null,
                null,
                [],
                [],
                Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("At least one country route is required.", result.Error);
        Assert.True(existingRoute.IsActive);
        Assert.Equal("Stripe Production", config.DisplayName);
        repository.Verify(
            repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Update_NullCountryCodes_LeavesExistingRoutesUnchanged()
    {
        var gatewayId = Guid.NewGuid();
        var existingRoute = new PaymentGatewayCountryRoute
        {
            Id = Guid.NewGuid(),
            GatewayConfigId = gatewayId,
            CountryCode = "LK",
            Environment = "production",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var config = new PaymentGatewayConfig
        {
            Id = gatewayId,
            GatewayKey = "stripe-prod",
            Provider = "stripe",
            Environment = "production",
            DisplayName = "Stripe Production",
            IsActive = true,
            CountryRoutes = [existingRoute]
        };
        var repository = UpdateRepository(gatewayId, config, []);
        var handler = new UpdatePaymentGatewayMetadataCommandHandler(repository.Object);

        var result = await handler.Handle(
            new UpdatePaymentGatewayMetadataCommand(
                gatewayId,
                "Stripe Production Updated",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(existingRoute.IsActive);
        Assert.Equal("Stripe Production Updated", config.DisplayName);
        repository.Verify(
            repo => repo.AddCountryRouteAsync(
                It.IsAny<PaymentGatewayCountryRoute>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        repository.Verify(
            repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Update_NotFound_Returns404()
    {
        var gatewayId = Guid.NewGuid();
        var repository = new Mock<IPaymentGatewayRepository>();
        repository
            .Setup(repo => repo.GetByIdAsync(
                gatewayId,
                false,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentGatewayConfig?)null);
        var handler = new UpdatePaymentGatewayMetadataCommandHandler(repository.Object);

        var result = await handler.Handle(
            new UpdatePaymentGatewayMetadataCommand(
                gatewayId,
                "Updated Name",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        repository.Verify(
            repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Update_EmptyDisplayName_Returns400()
    {
        var gatewayId = Guid.NewGuid();
        var config = new PaymentGatewayConfig
        {
            Id = gatewayId,
            GatewayKey = "stripe-prod",
            Provider = "stripe",
            Environment = "production",
            DisplayName = "Stripe Production",
            IsActive = true,
            CountryRoutes = []
        };
        var repository = UpdateRepository(gatewayId, config, []);
        var handler = new UpdatePaymentGatewayMetadataCommandHandler(repository.Object);

        var result = await handler.Handle(
            new UpdatePaymentGatewayMetadataCommand(
                gatewayId,
                "   ",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("DisplayName cannot be empty.", result.Error);
        Assert.Equal("Stripe Production", config.DisplayName);
        repository.Verify(
            repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Update_EmptyOptionalFields_ClearExistingValues()
    {
        var gatewayId = Guid.NewGuid();
        var config = new PaymentGatewayConfig
        {
            Id = gatewayId,
            GatewayKey = "stripe-prod",
            Provider = "stripe",
            Environment = "production",
            DisplayName = "Stripe Production",
            LogoUrl = "https://example.com/logo.png",
            PublicKey = "pk_live",
            MerchantId = "merchant-1",
            WebhookUrl = "https://example.com/webhook",
            IsActive = true,
            CountryRoutes = []
        };
        var repository = UpdateRepository(gatewayId, config, []);
        var handler = new UpdatePaymentGatewayMetadataCommandHandler(repository.Object);

        var result = await handler.Handle(
            new UpdatePaymentGatewayMetadataCommand(
                gatewayId,
                null,
                "",
                "",
                "",
                "",
                null,
                null,
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(config.LogoUrl);
        Assert.Null(config.PublicKey);
        Assert.Null(config.MerchantId);
        Assert.Null(config.WebhookUrl);
    }

    [Fact]
    public async Task Update_IsActiveTrue_ActivatesGateway()
    {
        var gatewayId = Guid.NewGuid();
        var config = new PaymentGatewayConfig
        {
            Id = gatewayId,
            GatewayKey = "stripe-prod",
            Provider = "stripe",
            Environment = "production",
            DisplayName = "Stripe Production",
            IsActive = false,
            CountryRoutes = []
        };
        var repository = UpdateRepository(gatewayId, config, []);
        var handler = new UpdatePaymentGatewayMetadataCommandHandler(repository.Object);

        var result = await handler.Handle(
            new UpdatePaymentGatewayMetadataCommand(
                gatewayId,
                null,
                null,
                null,
                null,
                null,
                true,
                null,
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(config.IsActive);
        Assert.True(result.Value!.IsActive);
    }

    [Fact]
    public async Task Update_InvalidCountryCode_Returns400_AndLeavesMetadataUnchanged()
    {
        var gatewayId = Guid.NewGuid();
        var config = new PaymentGatewayConfig
        {
            Id = gatewayId,
            GatewayKey = "stripe-prod",
            Provider = "stripe",
            Environment = "production",
            DisplayName = "Stripe Production",
            IsActive = true,
            CountryRoutes = []
        };
        var repository = UpdateRepository(gatewayId, config, []);
        var handler = new UpdatePaymentGatewayMetadataCommandHandler(repository.Object);

        var result = await handler.Handle(
            new UpdatePaymentGatewayMetadataCommand(
                gatewayId,
                "Updated Name",
                null,
                null,
                null,
                null,
                null,
                ["USA"],
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("Stripe Production", config.DisplayName);
        repository.Verify(
            repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Update_DuplicateCountryCode_Returns409()
    {
        var gatewayId = Guid.NewGuid();
        var config = new PaymentGatewayConfig
        {
            Id = gatewayId,
            GatewayKey = "stripe-prod",
            Provider = "stripe",
            Environment = "production",
            DisplayName = "Stripe Production",
            IsActive = true,
            CountryRoutes = []
        };
        var repository = UpdateRepository(gatewayId, config, []);
        var handler = new UpdatePaymentGatewayMetadataCommandHandler(repository.Object);

        var result = await handler.Handle(
            new UpdatePaymentGatewayMetadataCommand(
                gatewayId,
                null,
                null,
                null,
                null,
                null,
                null,
                ["US", "us"],
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        repository.Verify(
            repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Update_CountryRouteConflict_Returns409()
    {
        var gatewayId = Guid.NewGuid();
        var config = new PaymentGatewayConfig
        {
            Id = gatewayId,
            GatewayKey = "stripe-prod",
            Provider = "stripe",
            Environment = "production",
            DisplayName = "Stripe Production",
            IsActive = true,
            CountryRoutes = []
        };
        var repository = UpdateRepository(gatewayId, config, []);
        repository
            .Setup(repo => repo.HasConflictingCountryRouteAsync(
                "US",
                "production",
                gatewayId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var handler = new UpdatePaymentGatewayMetadataCommandHandler(repository.Object);

        var result = await handler.Handle(
            new UpdatePaymentGatewayMetadataCommand(
                gatewayId,
                null,
                null,
                null,
                null,
                null,
                null,
                ["US"],
                ["United States"],
                Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        repository.Verify(
            repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
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

    private static Mock<IPaymentGatewayRepository> UpdateRepository(
        Guid gatewayId,
        PaymentGatewayConfig config,
        List<PaymentGatewayCountryRoute> addedRoutes)
    {
        var repository = new Mock<IPaymentGatewayRepository>();
        repository
            .Setup(repo => repo.GetByIdAsync(
                gatewayId,
                false,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
        repository
            .Setup(repo => repo.HasConflictingCountryRouteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                gatewayId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repository
            .Setup(repo => repo.AddCountryRouteAsync(
                It.IsAny<PaymentGatewayCountryRoute>(),
                It.IsAny<CancellationToken>()))
            .Callback<PaymentGatewayCountryRoute, CancellationToken>(
                (route, _) => addedRoutes.Add(route))
            .Returns(Task.CompletedTask);
        repository
            .Setup(repo => repo.GetActiveCredentialAsync(
                gatewayId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayCredential
            {
                PaymentGatewayConfigId = gatewayId,
                CredentialVersion = 2,
                IsActive = true
            });
        repository
            .Setup(repo => repo.ListRoutesForConfigAsync(
                gatewayId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
                (IReadOnlyList<PaymentGatewayCountryRoute>)config.CountryRoutes
                    .Concat(addedRoutes)
                    .ToList());
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
