using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Seeders;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig;

/// <summary>
/// Covers the Development/Test-only approved OAuth provider metadata seed:
/// creates google/github/microsoft/zoom metadata rows only, is idempotent, never
/// writes credentials/secrets, and never overwrites an already-configured row.
/// </summary>
public sealed class PlatformOAuthProviderMetadataSeederTests
{
    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }

    private static async Task<PlatformUser> SeedBootstrapUserAsync(ApplicationDbContext db)
    {
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "bootstrap@onevo.test",
            FullName = "Bootstrap Admin",
            Status = PlatformUser.StatusActive,
            MfaStatus = PlatformUser.MfaNotEnrolled,
            InviteStatus = PlatformUser.InviteAccepted,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        db.PlatformUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task SeedAsync_NoBootstrapUser_DoesNothing()
    {
        await using var db = BuildInMemoryDb();

        await PlatformOAuthProviderMetadataSeeder.SeedAsync(db, CancellationToken.None);

        Assert.Empty(await db.PlatformOAuthApps.ToListAsync());
    }

    [Fact]
    public async Task SeedAsync_CreatesExactlyFourApprovedProviderMetadataRows()
    {
        await using var db = BuildInMemoryDb();
        await SeedBootstrapUserAsync(db);

        await PlatformOAuthProviderMetadataSeeder.SeedAsync(db, CancellationToken.None);

        var apps = await db.PlatformOAuthApps.ToListAsync();
        Assert.Equal(4, apps.Count);
        Assert.Equal(
            new[] { "github", "google", "microsoft", "zoom" },
            apps.Select(a => a.Provider).OrderBy(p => p));
        Assert.All(apps, a =>
        {
            Assert.Equal(string.Empty, a.ClientId);
            Assert.False(a.IsActive);
        });
        Assert.Empty(await db.PlatformOAuthAppCredentials.ToListAsync());
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_RunningTwiceDoesNotDuplicateOrChangeRows()
    {
        await using var db = BuildInMemoryDb();
        await SeedBootstrapUserAsync(db);

        await PlatformOAuthProviderMetadataSeeder.SeedAsync(db, CancellationToken.None);
        await PlatformOAuthProviderMetadataSeeder.SeedAsync(db, CancellationToken.None);

        var apps = await db.PlatformOAuthApps.ToListAsync();
        Assert.Equal(4, apps.Count);
    }

    [Fact]
    public async Task SeedAsync_DoesNotOverwriteExistingConfiguredRow()
    {
        await using var db = BuildInMemoryDb();
        var user = await SeedBootstrapUserAsync(db);

        db.PlatformOAuthApps.Add(new PlatformOAuthApp
        {
            Id = Guid.NewGuid(),
            Provider = "github",
            AppName = "Operator Configured GitHub App",
            LogoUrl = "https://cdn.onevo.app/logos/custom-github.png",
            ClientId = "operator-set-client-id",
            AuthorizationUrl = "https://github.com/login/oauth/authorize",
            TokenUrl = "https://github.com/login/oauth/access_token",
            DefaultScopes = new[] { "read:user" },
            IsActive = true,
            UpdatedById = user.Id,
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        await PlatformOAuthProviderMetadataSeeder.SeedAsync(db, CancellationToken.None);

        var github = await db.PlatformOAuthApps.SingleAsync(a => a.Provider == "github");
        Assert.Equal("Operator Configured GitHub App", github.AppName);
        Assert.Equal("operator-set-client-id", github.ClientId);
        Assert.Equal("https://cdn.onevo.app/logos/custom-github.png", github.LogoUrl);
        Assert.True(github.IsActive);

        // Other three providers still get seeded as metadata-only cards.
        var others = await db.PlatformOAuthApps.Where(a => a.Provider != "github").ToListAsync();
        Assert.Equal(3, others.Count);
        Assert.All(others, a => Assert.Equal(string.Empty, a.ClientId));
    }
}
