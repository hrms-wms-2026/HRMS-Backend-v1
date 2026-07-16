using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.Identity;

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
            .WithMessage("*process-local*");
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
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task DisallowedEnvironment_SharedStore_FlagFalse_DoesNotThrow(string environmentName)
    {
        var guard = CreateGuard(new FakeSharedMfaChallengeStore(), environmentName, allowProcessLocal: false);

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

    private static MfaChallengeStoreStartupGuard CreateGuard(
        IMfaChallengeStore store,
        string environmentName,
        bool allowProcessLocal)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(environmentName);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [MfaChallengeStoreStartupGuard.AllowProcessLocalConfigKey] = allowProcessLocal.ToString()
            })
            .Build();

        return new MfaChallengeStoreStartupGuard(
            store,
            environment.Object,
            configuration,
            Mock.Of<ILogger<MfaChallengeStoreStartupGuard>>());
    }

    private sealed class FakeSharedMfaChallengeStore : IMfaChallengeStore
    {
        public Task<string> CreateAsync(Guid userId, Guid tenantId, TimeSpan lifetime, CancellationToken ct = default)
            => Task.FromResult("challenge");

        public Task<MfaChallengeState?> GetAsync(string challenge, CancellationToken ct = default)
            => Task.FromResult<MfaChallengeState?>(null);

        public Task<bool> RegisterFailedAttemptAsync(string challenge, int maximumAttempts, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<MfaChallengeState?> TryConsumeAsync(string challenge, CancellationToken ct = default)
            => Task.FromResult<MfaChallengeState?>(null);
    }
}
