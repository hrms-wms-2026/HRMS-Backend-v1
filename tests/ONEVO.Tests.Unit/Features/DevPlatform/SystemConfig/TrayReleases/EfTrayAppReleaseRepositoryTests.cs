using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.TrayReleases.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.SystemConfig;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig.TrayReleases;

public sealed class EfTrayAppReleaseRepositoryTests
{
    private static ApplicationDbContext BuildInMemoryDb()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();

        return new ApplicationDbContext(
            optionsBuilder.Options,
            new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }

    private static TrayAppRelease Make(string version, string channel = "stable", bool active = true) => new()
    {
        Id = Guid.NewGuid(),
        Version = version,
        Channel = channel,
        DownloadUrl = $"https://dl.example.com/onevo-{version}.msix",
        Sha256 = new string('a', 64),
        FileSizeBytes = 1234,
        Publisher = "CN=ONEVO",
        MinimumWindowsVersion = "10.0.19041.0",
        IsActive = active,
        Source = "admin"
    };

    [Fact]
    public async Task GetLatestActive_ReturnsHighestNumericVersion_NotLexicographic()
    {
        await using var db = BuildInMemoryDb();
        db.TrayAppReleases.AddRange(Make("1.9.0"), Make("1.10.0"), Make("1.2.0"));
        await db.SaveChangesAsync();

        var latest = await new EfTrayAppReleaseRepository(db)
            .GetLatestActiveAsync("stable", CancellationToken.None);

        Assert.Equal("1.10.0", latest!.Version);
    }

    [Fact]
    public async Task GetLatestActive_IgnoresInactiveAndOtherChannels()
    {
        await using var db = BuildInMemoryDb();
        db.TrayAppReleases.AddRange(
            Make("2.0.0", active: false),
            Make("3.0.0", channel: "beta"),
            Make("1.0.0"));
        await db.SaveChangesAsync();

        var latest = await new EfTrayAppReleaseRepository(db)
            .GetLatestActiveAsync("stable", CancellationToken.None);

        Assert.Equal("1.0.0", latest!.Version);
    }

    [Fact]
    public async Task GetLatestActive_ReturnsNull_WhenNothingActive()
    {
        await using var db = BuildInMemoryDb();
        db.TrayAppReleases.Add(Make("1.0.0", active: false));
        await db.SaveChangesAsync();

        Assert.Null(await new EfTrayAppReleaseRepository(db)
            .GetLatestActiveAsync("stable", CancellationToken.None));
    }

    [Fact]
    public async Task GetByChannelAndVersion_FindsExactMatch()
    {
        await using var db = BuildInMemoryDb();
        db.TrayAppReleases.AddRange(Make("1.0.0"), Make("1.0.0", channel: "beta"));
        await db.SaveChangesAsync();

        var hit = await new EfTrayAppReleaseRepository(db)
            .GetByChannelAndVersionAsync("beta", "1.0.0", CancellationToken.None);

        Assert.Equal("beta", hit!.Channel);
    }

    [Fact]
    public async Task AddAsync_DoesNotSaveAutomatically()
    {
        await using var db = BuildInMemoryDb();
        var repo = new EfTrayAppReleaseRepository(db);
        var release = Make("1.0.0");

        await repo.AddAsync(release, CancellationToken.None);

        Assert.Equal(EntityState.Added, db.Entry(release).State);
        Assert.Equal(0, await db.TrayAppReleases.CountAsync());
    }
}
