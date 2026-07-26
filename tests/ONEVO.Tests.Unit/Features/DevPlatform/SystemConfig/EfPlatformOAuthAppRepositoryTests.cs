using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.SystemConfig;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig;

public sealed class EfPlatformOAuthAppRepositoryTests
{
    [Fact]
    public async Task ListAllAsync_OrdersByProviderAndDoesNotTrackResults()
    {
        await using var db = BuildInMemoryDb();
        db.PlatformOAuthApps.AddRange(
            CreateApp("zoom"),
            CreateApp("github"),
            CreateApp("microsoft"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPlatformOAuthAppRepository(db);

        var apps = await repository.ListAllAsync(CancellationToken.None);

        Assert.Equal(
            ["github", "microsoft", "zoom"],
            apps.Select(app => app.Provider).ToArray());
        Assert.Empty(db.ChangeTracker.Entries<PlatformOAuthApp>());
    }

    [Fact]
    public async Task GetByProviderAsync_FiltersByProvider()
    {
        await using var db = BuildInMemoryDb();
        var expected = CreateApp("github");
        db.PlatformOAuthApps.AddRange(
            expected,
            CreateApp("google"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPlatformOAuthAppRepository(db);

        var app = await repository.GetByProviderAsync(
            "github",
            CancellationToken.None);

        Assert.NotNull(app);
        Assert.Equal(expected.Id, app.Id);
        Assert.Contains(
            db.ChangeTracker.Entries<PlatformOAuthApp>(),
            entry => entry.Entity.Id == expected.Id);
    }

    [Fact]
    public async Task GetActiveCredentialsForAppAsync_FiltersByAppAndActiveStatus()
    {
        await using var db = BuildInMemoryDb();
        var platformOAuthAppId = Guid.NewGuid();
        var expected = CreateCredential(
            platformOAuthAppId,
            isActive: true,
            version: 1);
        db.PlatformOAuthAppCredentials.AddRange(
            expected,
            CreateCredential(platformOAuthAppId, isActive: false, version: 2),
            CreateCredential(Guid.NewGuid(), isActive: true, version: 1));
        await db.SaveChangesAsync();

        var repository = new EfPlatformOAuthAppRepository(db);

        var credentials = await repository.GetActiveCredentialsForAppAsync(
            platformOAuthAppId,
            CancellationToken.None);

        Assert.Single(credentials);
        Assert.Equal(expected.Id, credentials[0].Id);
    }

    [Fact]
    public async Task ListActiveCredentialsAsync_ReturnsAllActiveRowsWithoutTracking()
    {
        await using var db = BuildInMemoryDb();
        var firstActive = CreateCredential(
            Guid.NewGuid(),
            isActive: true,
            version: 1);
        var secondActive = CreateCredential(
            Guid.NewGuid(),
            isActive: true,
            version: 1);
        db.PlatformOAuthAppCredentials.AddRange(
            firstActive,
            secondActive,
            CreateCredential(Guid.NewGuid(), isActive: false, version: 1));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPlatformOAuthAppRepository(db);

        var credentials = await repository.ListActiveCredentialsAsync(
            CancellationToken.None);

        Assert.Equal(2, credentials.Count);
        Assert.Contains(
            credentials,
            credential => credential.Id == firstActive.Id);
        Assert.Contains(
            credentials,
            credential => credential.Id == secondActive.Id);
        Assert.Empty(db.ChangeTracker.Entries<PlatformOAuthAppCredential>());
    }

    [Fact]
    public async Task SaveChangesAsync_DelegatesToDbContextWithCancellationToken()
    {
        var observer = new SaveChangesObserver();
        await using var db = BuildInMemoryDb(observer);
        var repository = new EfPlatformOAuthAppRepository(db);
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

    private static PlatformOAuthApp CreateApp(string provider)
    {
        return new PlatformOAuthApp
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            AppName = $"ONEVO for {provider}",
            ClientId = $"{provider}-client-id",
            AuthorizationUrl = $"https://{provider}.example/authorize",
            TokenUrl = $"https://{provider}.example/token",
            DefaultScopes = ["read"],
            IsActive = true,
            UpdatedById = Guid.NewGuid()
        };
    }

    private static PlatformOAuthAppCredential CreateCredential(
        Guid platformOAuthAppId,
        bool isActive,
        int version)
    {
        return new PlatformOAuthAppCredential
        {
            Id = Guid.NewGuid(),
            PlatformOAuthAppId = platformOAuthAppId,
            ClientSecretEncrypted = "encrypted-secret",
            EncryptionKeyVersion = "v1",
            CredentialVersion = version,
            IsActive = isActive,
            RotatedById = Guid.NewGuid()
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
