using FluentAssertions;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.ExternalServices.Email;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Infrastructure.Email;

/// <summary>
/// Plan D Task 3: <c>Email:MaxPerSecond</c> paces calendar invite emails. The limiter is per instance, not
/// cluster-wide, and <c>0</c> means unlimited.
/// </summary>
public sealed class TokenBucketEmailRateLimiterTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private static TokenBucketEmailRateLimiter Limiter(int maxPerSecond, TimeProvider clock)
        => new(Options.Create(new EmailRateOptions { MaxPerSecond = maxPerSecond }), clock);

    [Fact]
    public async Task Zero_NeverWaits()
    {
        var clock = new TestTimeProvider(Start);
        var limiter = Limiter(0, clock);

        for (var i = 0; i < 50; i++)
            await limiter.WaitAsync(CancellationToken.None);

        // The clock never moved and no timer was ever created: nothing was waited on.
        clock.GetUtcNow().Should().Be(Start);
        clock.TimerCount.Should().Be(0);
    }

    [Fact]
    public async Task Two_PerSecond_TheThirdWaitsForTheNextSecond()
    {
        var clock = new TestTimeProvider(Start);
        var limiter = Limiter(2, clock);

        // The first two go straight through.
        await limiter.WaitAsync(CancellationToken.None);
        await limiter.WaitAsync(CancellationToken.None);
        clock.TimerCount.Should().Be(0);

        // The third has to wait for the first of the two to fall out of the window.
        var third = limiter.WaitAsync(CancellationToken.None);
        await clock.WaitForTimerCountAsync(1);
        third.IsCompleted.Should().BeFalse("the third send is over the limit and must wait");

        clock.Advance(TimeSpan.FromSeconds(1));

        await third;
        third.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task TheWindow_Slides_SoTheLimitIsNotFixedToTheFirstSecond()
    {
        var clock = new TestTimeProvider(Start);
        var limiter = Limiter(2, clock);

        await limiter.WaitAsync(CancellationToken.None);
        await limiter.WaitAsync(CancellationToken.None);

        // The third waits for the first of the two to age out.
        var third = limiter.WaitAsync(CancellationToken.None);
        await clock.WaitForTimerCountAsync(1);
        third.IsCompleted.Should().BeFalse();

        clock.Advance(TimeSpan.FromSeconds(1));
        await third;

        // The window has slid: it holds the second send and the third, so one more fits and the next waits.
        var fourth = limiter.WaitAsync(CancellationToken.None);
        clock.TimerCount.Should().Be(1, "the fourth send fits in the slid window and does not wait");

        var fifth = limiter.WaitAsync(CancellationToken.None);
        await clock.WaitForTimerCountAsync(2);
        fifth.IsCompleted.Should().BeFalse("the fifth send is over the limit");

        clock.Advance(TimeSpan.FromSeconds(1));
        await fifth;
    }

    [Fact]
    public async Task One_PerSecond_EverySendAfterTheFirstWaits()
    {
        var clock = new TestTimeProvider(Start);
        var limiter = Limiter(1, clock);

        await limiter.WaitAsync(CancellationToken.None);

        var second = limiter.WaitAsync(CancellationToken.None);
        await clock.WaitForTimerCountAsync(1);
        second.IsCompleted.Should().BeFalse();

        clock.Advance(TimeSpan.FromSeconds(1));
        await second;
    }

    [Fact]
    public async Task ANegativeLimit_IsTreatedAsUnlimited()
    {
        // The startup validator rejects a negative limit, so this never happens in the running app. The
        // clamp is the guard for a container built without that validation: a negative window would
        // reject every send, which is worse than not pacing at all.
        var clock = new TestTimeProvider(Start);
        var limiter = Limiter(-5, clock);

        for (var i = 0; i < 10; i++)
            await limiter.WaitAsync(CancellationToken.None);

        clock.TimerCount.Should().Be(0);
    }

    [Fact]
    public async Task Cancellation_StopsTheWait()
    {
        var clock = new TestTimeProvider(Start);
        var limiter = Limiter(1, clock);

        await limiter.WaitAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var waiting = limiter.WaitAsync(cts.Token);
        await clock.WaitForTimerCountAsync(1);
        waiting.IsCompleted.Should().BeFalse();

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);
    }
}
