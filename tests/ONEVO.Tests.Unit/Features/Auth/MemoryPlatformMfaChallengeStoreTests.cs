using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Infrastructure.Identity.Mfa;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class MemoryPlatformMfaChallengeStoreTests
{
    private readonly Mock<ISecureTokenGenerator> _tokens = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly DateTimeOffset _now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    public MemoryPlatformMfaChallengeStoreTests()
    {
        _clock.Setup(value => value.UtcNow).Returns(() => _now);
        _tokens.Setup(value => value.GenerateOpaqueToken()).Returns("raw-challenge");
        _tokens.Setup(value => value.HashToken(It.IsAny<string>())).Returns<string>(raw => "hash:" + raw);
    }

    private MemoryPlatformMfaChallengeStore Build() =>
        new(new MemoryCache(new MemoryCacheOptions()), _tokens.Object, _clock.Object);

    [Fact]
    public async Task GoogleLinkChallenge_CarriesPendingGoogleSubToConsume()
    {
        var store = Build();
        var userId = Guid.NewGuid();

        var challenge = await store.CreateForGoogleLinkAsync(userId, "google-sub-123", TimeSpan.FromMinutes(10));
        var consumed = await store.TryConsumeAsync(challenge);

        consumed.Should().NotBeNull();
        consumed!.PlatformUserId.Should().Be(userId);
        consumed.PendingGoogleSub.Should().Be("google-sub-123");
    }

    [Fact]
    public async Task GoogleLinkChallenge_KeepsPendingGoogleSubAfterFailedAttempt()
    {
        var store = Build();

        var challenge = await store.CreateForGoogleLinkAsync(Guid.NewGuid(), "google-sub-123", TimeSpan.FromMinutes(10));
        await store.RegisterFailedAttemptAsync(challenge, maximumAttempts: 5);
        var state = await store.GetAsync(challenge);

        state.Should().NotBeNull();
        state!.FailedAttempts.Should().Be(1);
        state.PendingGoogleSub.Should().Be("google-sub-123");
    }

    [Fact]
    public async Task PlainChallenge_HasNoPendingGoogleSub()
    {
        var store = Build();

        var challenge = await store.CreateAsync(Guid.NewGuid(), TimeSpan.FromMinutes(10));
        var consumed = await store.TryConsumeAsync(challenge);

        consumed.Should().NotBeNull();
        consumed!.PendingGoogleSub.Should().BeNull();
    }
}
