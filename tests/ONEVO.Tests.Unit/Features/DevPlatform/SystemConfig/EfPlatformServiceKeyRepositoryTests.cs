using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.SystemConfig;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig;

public sealed class EfPlatformServiceKeyRepositoryTests
{
    [Fact]
    public async Task ListAllAsync_OrdersByServiceKeyAndDoesNotTrackResults()
    {
        await using var db = BuildInMemoryDb();
        db.PlatformServiceKeys.AddRange(
            CreateKey("sendgrid"),
            CreateKey("cloudflare"),
            CreateKey("resend"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPlatformServiceKeyRepository(db);

        var keys = await repository.ListAllAsync(CancellationToken.None);

        Assert.Equal(
            ["cloudflare", "resend", "sendgrid"],
            keys.Select(key => key.ServiceKey).ToArray());
        Assert.Empty(db.ChangeTracker.Entries<PlatformServiceKey>());
    }

    [Fact]
    public async Task GetByServiceKeyAsync_FiltersByExactServiceKeyAndTracksResult()
    {
        await using var db = BuildInMemoryDb();
        var expected = CreateKey("resend");
        db.PlatformServiceKeys.AddRange(
            expected,
            CreateKey("sendgrid"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPlatformServiceKeyRepository(db);

        var key = await repository.GetByServiceKeyAsync(
            "resend",
            CancellationToken.None);

        Assert.NotNull(key);
        Assert.Equal(expected.Id, key.Id);
        Assert.Contains(
            db.ChangeTracker.Entries<PlatformServiceKey>(),
            entry => entry.Entity.Id == expected.Id);
    }

    [Fact]
    public async Task GetByServiceKeyAsync_UnknownServiceKey_ReturnsNull()
    {
        await using var db = BuildInMemoryDb();
        db.PlatformServiceKeys.Add(CreateKey("resend"));
        await db.SaveChangesAsync();

        var repository = new EfPlatformServiceKeyRepository(db);

        var key = await repository.GetByServiceKeyAsync(
            "cloudflare",
            CancellationToken.None);

        Assert.Null(key);
    }

    [Fact]
    public async Task AddAsync_AddsEntityButDoesNotSaveAutomatically()
    {
        await using var db = BuildInMemoryDb();
        var repository = new EfPlatformServiceKeyRepository(db);
        var key = CreateKey("resend");

        await repository.AddAsync(key, CancellationToken.None);

        Assert.Equal(EntityState.Added, db.Entry(key).State);
        Assert.Equal(0, await db.PlatformServiceKeys.CountAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_DelegatesToDbContextWithCancellationToken()
    {
        var observer = new SaveChangesObserver();
        await using var db = BuildInMemoryDb(observer);
        var repository = new EfPlatformServiceKeyRepository(db);
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

    private static PlatformServiceKey CreateKey(string serviceKey)
    {
        return new PlatformServiceKey
        {
            Id = Guid.NewGuid(),
            ServiceKey = serviceKey,
            DisplayName = $"ONEVO {serviceKey}",
            ApiKeyEncrypted = "encrypted-value",
            IsActive = true,
            UpdatedById = Guid.NewGuid()
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
