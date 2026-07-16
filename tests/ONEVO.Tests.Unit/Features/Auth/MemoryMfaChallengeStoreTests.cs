using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Infrastructure.Identity;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class MemoryMfaChallengeStoreTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow.AddHours(1);

    [Fact]
    public async Task Challenge_IsOpaqueTenantBoundAndSingleUse()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var store = CreateStore("opaque-random-value", Now);

        var challenge = await store.CreateAsync(userId, tenantId, TimeSpan.FromMinutes(10));
        var state = await store.GetAsync(challenge);
        var consumed = await store.TryConsumeAsync(challenge);

        challenge.Should().Be("opaque-random-value");
        challenge.Should().NotContain(".");
        state.Should().Be(new MfaChallengeState(userId, tenantId, Now.AddMinutes(10), 0));
        consumed.Should().Be(state);
        (await store.GetAsync(challenge)).Should().BeNull();
        (await store.TryConsumeAsync(challenge)).Should().BeNull();
    }

    [Fact]
    public async Task Challenge_ExpiresAndCannotBeConsumed()
    {
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(instance => instance.UtcNow).Returns(() => _currentTime);
        _currentTime = Now;
        var store = CreateStore("expiring", clock.Object);
        var challenge = await store.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromMinutes(10));

        _currentTime = Now.AddMinutes(11);

        (await store.GetAsync(challenge)).Should().BeNull();
        (await store.TryConsumeAsync(challenge)).Should().BeNull();
    }

    [Fact]
    public async Task MaximumFailedAttempts_InvalidatesChallenge()
    {
        var store = CreateStore("limited", Now);
        var challenge = await store.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromMinutes(10));

        (await store.RegisterFailedAttemptAsync(challenge, 3)).Should().BeTrue();
        (await store.RegisterFailedAttemptAsync(challenge, 3)).Should().BeTrue();
        (await store.RegisterFailedAttemptAsync(challenge, 3)).Should().BeFalse();
        (await store.GetAsync(challenge)).Should().BeNull();
    }

    private DateTimeOffset _currentTime;

    private static MemoryMfaChallengeStore CreateStore(string rawToken, DateTimeOffset now)
    {
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(instance => instance.UtcNow).Returns(now);
        return CreateStore(rawToken, clock.Object);
    }

    private static MemoryMfaChallengeStore CreateStore(string rawToken, IDateTimeProvider clock)
    {
        var tokens = new Mock<ISecureTokenGenerator>();
        tokens.Setup(instance => instance.GenerateOpaqueToken()).Returns(rawToken);
        tokens.Setup(instance => instance.HashToken(It.IsAny<string>()))
            .Returns((string value) => "hash:" + value);

        return new MemoryMfaChallengeStore(
            new MemoryCache(new MemoryCacheOptions()),
            tokens.Object,
            clock);
    }
}
