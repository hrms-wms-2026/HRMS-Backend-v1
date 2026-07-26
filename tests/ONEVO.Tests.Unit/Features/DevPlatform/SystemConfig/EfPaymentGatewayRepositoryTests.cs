using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.SystemConfig;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig;

public sealed class EfPaymentGatewayRepositoryTests
{
    [Fact]
    public async Task GetByGatewayKeyAsync_FindsConfigByGatewayKey()
    {
        await using var db = BuildInMemoryDb();
        var expected = CreateConfig("stripe-primary");
        db.PaymentGatewayConfigs.AddRange(
            expected,
            CreateConfig("stripe-secondary"));
        await db.SaveChangesAsync();

        var repository = new EfPaymentGatewayRepository(db);

        var config = await repository.GetByGatewayKeyAsync(
            "stripe-primary",
            CancellationToken.None);

        Assert.NotNull(config);
        Assert.Equal(expected.Id, config.Id);
    }

    [Fact]
    public async Task GetActiveCredentialAsync_FiltersByConfigAndActiveStatus()
    {
        await using var db = BuildInMemoryDb();
        var gatewayConfigId = Guid.NewGuid();
        var expected = CreateCredential(gatewayConfigId, isActive: true, version: 1);
        db.PaymentGatewayCredentials.AddRange(
            expected,
            CreateCredential(gatewayConfigId, isActive: false, version: 2),
            CreateCredential(Guid.NewGuid(), isActive: true, version: 1));
        await db.SaveChangesAsync();

        var repository = new EfPaymentGatewayRepository(db);

        var credential = await repository.GetActiveCredentialAsync(
            gatewayConfigId,
            CancellationToken.None);

        Assert.NotNull(credential);
        Assert.Equal(expected.Id, credential.Id);
    }

    [Fact]
    public async Task GetActiveCredentialsAsync_ReturnsAllActiveRowsForConfig()
    {
        await using var db = BuildInMemoryDb();
        var gatewayConfigId = Guid.NewGuid();
        var firstActive = CreateCredential(gatewayConfigId, isActive: true, version: 1);
        var secondActive = CreateCredential(gatewayConfigId, isActive: true, version: 2);
        db.PaymentGatewayCredentials.AddRange(
            firstActive,
            secondActive,
            CreateCredential(gatewayConfigId, isActive: false, version: 3),
            CreateCredential(Guid.NewGuid(), isActive: true, version: 1));
        await db.SaveChangesAsync();

        var repository = new EfPaymentGatewayRepository(db);

        var credentials = await repository.GetActiveCredentialsAsync(
            gatewayConfigId,
            CancellationToken.None);

        Assert.Equal(2, credentials.Count);
        Assert.Contains(credentials, credential => credential.Id == firstActive.Id);
        Assert.Contains(credentials, credential => credential.Id == secondActive.Id);
    }

    [Fact]
    public async Task HasConflictingCountryRouteAsync_UsesCountryEnvironmentActiveAndExcludedConfig()
    {
        await using var db = BuildInMemoryDb();
        var excludedGatewayConfigId = Guid.NewGuid();
        db.PaymentGatewayCountryRoutes.AddRange(
            CreateRoute(excludedGatewayConfigId, "LK", "production", isActive: true),
            CreateRoute(Guid.NewGuid(), "LK", "production", isActive: false),
            CreateRoute(Guid.NewGuid(), "LK", "sandbox", isActive: true),
            CreateRoute(Guid.NewGuid(), "US", "production", isActive: true));
        await db.SaveChangesAsync();

        var repository = new EfPaymentGatewayRepository(db);

        var existsBeforeConflict = await repository.HasConflictingCountryRouteAsync(
            "LK",
            "production",
            excludedGatewayConfigId,
            CancellationToken.None);

        Assert.False(existsBeforeConflict);

        db.PaymentGatewayCountryRoutes.Add(
            CreateRoute(Guid.NewGuid(), "LK", "production", isActive: true));
        await db.SaveChangesAsync();

        var existsAfterConflict = await repository.HasConflictingCountryRouteAsync(
            "LK",
            "production",
            excludedGatewayConfigId,
            CancellationToken.None);

        Assert.True(existsAfterConflict);
    }

    [Fact]
    public async Task SaveChangesAsync_DelegatesToDbContextWithCancellationToken()
    {
        var observer = new SaveChangesObserver();
        await using var db = BuildInMemoryDb(observer);
        var repository = new EfPaymentGatewayRepository(db);
        using var cancellationSource = new CancellationTokenSource();

        await repository.SaveChangesAsync(cancellationSource.Token);

        Assert.Equal(1, observer.CallCount);
        Assert.Equal(cancellationSource.Token, observer.CancellationToken);
    }

    private static ApplicationDbContext BuildInMemoryDb(
        SaveChangesObserver? observer = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());

        if (observer is not null)
        {
            optionsBuilder.AddInterceptors(observer);
        }

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();

        var auditInterceptor = new AuditableEntityInterceptor(
            currentUser.Object,
            dateTimeProvider.Object);
        var softDeleteInterceptor = new SoftDeleteInterceptor(dateTimeProvider.Object);
        var domainEventInterceptor = new DomainEventDispatchInterceptor(publisher.Object);

        return new ApplicationDbContext(
            optionsBuilder.Options,
            auditInterceptor,
            softDeleteInterceptor,
            domainEventInterceptor,
            tenantContext.Object);
    }

    private static PaymentGatewayConfig CreateConfig(string gatewayKey)
    {
        return new PaymentGatewayConfig
        {
            Id = Guid.NewGuid(),
            GatewayKey = gatewayKey,
            Provider = "stripe",
            Environment = "production",
            DisplayName = gatewayKey,
            IsActive = true
        };
    }

    private static PaymentGatewayCredential CreateCredential(
        Guid gatewayConfigId,
        bool isActive,
        int version)
    {
        return new PaymentGatewayCredential
        {
            Id = Guid.NewGuid(),
            PaymentGatewayConfigId = gatewayConfigId,
            SecretEncrypted = [1, 2, 3],
            EncryptionKeyVersion = "v1",
            CredentialVersion = version,
            IsActive = isActive,
            RotatedById = Guid.NewGuid()
        };
    }

    private static PaymentGatewayCountryRoute CreateRoute(
        Guid gatewayConfigId,
        string countryCode,
        string environment,
        bool isActive)
    {
        return new PaymentGatewayCountryRoute
        {
            Id = Guid.NewGuid(),
            GatewayConfigId = gatewayConfigId,
            CountryCode = countryCode,
            Environment = environment,
            IsActive = isActive
        };
    }

    private sealed class SaveChangesObserver : SaveChangesInterceptor
    {
        public int CallCount { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            CancellationToken = cancellationToken;

            return base.SavingChangesAsync(
                eventData,
                result,
                cancellationToken);
        }
    }
}
