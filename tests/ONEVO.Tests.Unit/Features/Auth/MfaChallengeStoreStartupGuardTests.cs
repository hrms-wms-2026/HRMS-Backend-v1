using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class MfaChallengeStoreStartupGuardTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public async Task AllowedEnvironment_MemoryStore_DoesNotThrow(string environmentName)
    {
        var guard = CreateGuard(CreateMemoryStore(), environmentName, allowProcessLocal: false);

        var act = () => guard.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task DisallowedEnvironment_MemoryStore_FlagFalse_Throws(string environmentName)
    {
        var guard = CreateGuard(CreateMemoryStore(), environmentName, allowProcessLocal: false);

        var act = () => guard.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*PostgreSQL-backed mfa_challenges*");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task DisallowedEnvironment_MemoryStore_FlagTrue_DoesNotThrow(string environmentName)
    {
        var guard = CreateGuard(CreateMemoryStore(), environmentName, allowProcessLocal: true);

        var act = () => guard.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task AnyEnvironment_PostgresStore_FlagFalse_DoesNotThrow(string environmentName)
    {
        var guard = CreateGuard(CreatePostgresStore(), environmentName, allowProcessLocal: false);

        var act = () => guard.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private static MemoryMfaChallengeStore CreateMemoryStore()
    {
        var tokens = new Mock<ISecureTokenGenerator>();
        var clock = new Mock<IDateTimeProvider>();
        return new MemoryMfaChallengeStore(
            new MemoryCache(new MemoryCacheOptions()),
            tokens.Object,
            clock.Object);
    }

    private static PostgresMfaChallengeStore CreatePostgresStore()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var clock = new SystemDateTimeProvider();
        var db = new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), clock),
            new SoftDeleteInterceptor(clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());

        return new PostgresMfaChallengeStore(db, new SecureTokenGenerator(), clock);
    }

    private static MfaChallengeStoreStartupGuard CreateGuard(
        IMfaChallengeStore store,
        string environmentName,
        bool allowProcessLocal)
    {
        var services = new ServiceCollection();
        services.AddScoped<IMfaChallengeStore>(_ => store);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(environmentName);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [MfaChallengeStoreStartupGuard.AllowProcessLocalConfigKey] = allowProcessLocal.ToString()
            })
            .Build();

        return new MfaChallengeStoreStartupGuard(
            scopeFactory,
            environment.Object,
            configuration,
            Mock.Of<ILogger<MfaChallengeStoreStartupGuard>>());
    }
}
