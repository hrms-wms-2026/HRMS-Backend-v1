using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Outbox;

/// <summary>
/// Pure state transitions of the claim-based outbox: retry/backoff after a failed attempt, the
/// lease and handler time budget, per-instance claim owner ids, and the per-dispatch provider
/// idempotency keys. The SQL that applies them atomically is covered by the Testcontainers
/// OutboxProcessor*IntegrationTests.
/// </summary>
public sealed class OutboxStateTransitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    // Failure -> retry / park

    [Fact]
    public void FirstFailure_ReturnsTheMessageToPending_WithThe30SecondBaseBackoff()
    {
        var outcome = OutboxRetryPolicy.OnFailure(attemptCountBefore: 0, new InvalidOperationException("boom"), Now);

        Assert.Equal(OutboxMessageStatus.Pending, outcome.Status);
        Assert.Equal(1, outcome.AttemptCount);
        Assert.Equal(Now.AddSeconds(30), outcome.NextAttemptAt);
        Assert.Null(outcome.ProcessedAt);
        Assert.Equal("boom", outcome.LastError);
    }

    [Theory]
    [InlineData(1, 60)]
    [InlineData(2, 120)]
    [InlineData(3, 240)]
    [InlineData(4, 480)]
    [InlineData(5, 960)]
    [InlineData(6, 1920)]
    public void LaterFailures_DoubleTheBackoff_ExactlyAsBeforeClaiming(int attemptCountBefore, int expectedDelaySeconds)
    {
        var outcome = OutboxRetryPolicy.OnFailure(attemptCountBefore, new Exception("x"), Now);

        Assert.Equal(OutboxMessageStatus.Pending, outcome.Status);
        Assert.Equal(attemptCountBefore + 1, outcome.AttemptCount);
        Assert.Equal(Now.AddSeconds(expectedDelaySeconds), outcome.NextAttemptAt);
    }

    [Fact]
    public void FailureThatReachesMaxAttempts_ParksTheMessageAsFailed_WithoutANextAttempt()
    {
        var outcome = OutboxRetryPolicy.OnFailure(OutboxRetryPolicy.MaxAttempts - 1, new Exception("last"), Now);

        Assert.Equal(8, OutboxRetryPolicy.MaxAttempts);
        Assert.Equal(OutboxMessageStatus.Failed, outcome.Status);
        Assert.Equal(OutboxRetryPolicy.MaxAttempts, outcome.AttemptCount);
        Assert.Equal(Now, outcome.ProcessedAt);
        Assert.Null(outcome.NextAttemptAt);
    }

    [Fact]
    public void FailureError_IsTruncatedToTheLastErrorColumnLength()
    {
        var outcome = OutboxRetryPolicy.OnFailure(0, new Exception(new string('e', 5000)), Now);

        Assert.Equal(2000, outcome.LastError.Length);
    }

    // Reclaimed after an abandoned attempt (lease expired)

    [Theory]
    [InlineData(0, false)]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(9, true)]
    public void AReclaimedMessage_IsParkedInsteadOfDispatched_OnceItsAbandonedAttemptsReachMaxAttempts(int attemptCount, bool park)
    {
        Assert.Equal(park, OutboxRetryPolicy.ShouldPark(attemptCount));
    }

    [Fact]
    public void Parking_MarksTheMessageFailed_KeepingItsCountAndReason()
    {
        var outcome = OutboxRetryPolicy.Park(attemptCount: 8, lastError: "lease expired", Now);

        Assert.Equal(OutboxMessageStatus.Failed, outcome.Status);
        Assert.Equal(8, outcome.AttemptCount);
        Assert.Equal(Now, outcome.ProcessedAt);
        Assert.Null(outcome.NextAttemptAt);
        Assert.Equal("lease expired", outcome.LastError);
    }

    // Lease and handler time budget

    [Fact]
    public void DefaultLease_IsFiveMinutes_AndTheHandlerBudgetEndsWellBeforeIt()
    {
        var lease = OutboxLeaseSettings.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.Equal(TimeSpan.FromMinutes(5), lease.Lease);
        Assert.Equal(TimeSpan.FromMinutes(4), lease.HandlerTimeout);
        // The provider HTTP call is bounded by HttpClient's default 100 s timeout.
        Assert.True(lease.HandlerTimeout > TimeSpan.FromSeconds(100));
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    [InlineData(600, 600)]
    public void ConfiguredLease_IsClampedToAtLeastOneSecond_AndTheHandlerBudgetIsAlwaysShorter(int configured, int expectedSeconds)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Outbox:LeaseSeconds"] = configured.ToString() })
            .Build();

        var lease = OutboxLeaseSettings.FromConfiguration(configuration);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), lease.Lease);
        Assert.True(lease.HandlerTimeout > TimeSpan.Zero);
        Assert.True(lease.HandlerTimeout < lease.Lease);
    }

    // Claim owner ids

    [Fact]
    public void EveryProcessorInstance_ClaimsUnderItsOwnOwnerId_ThatFitsTheLockedByColumn()
    {
        var configuration = new ConfigurationBuilder().Build();
        var a = new OutboxProcessor(Mock.Of<IServiceProvider>(), configuration, NullLogger<OutboxProcessor>.Instance);
        var b = new OutboxProcessor(Mock.Of<IServiceProvider>(), configuration, NullLogger<OutboxProcessor>.Instance);

        Assert.NotEqual(a.InstanceId, b.InstanceId);
        Assert.InRange(a.InstanceId.Length, 1, OutboxMessage.LockedByMaxLength);
    }

    [Fact]
    public void ProcessingStatus_FitsTheStatusColumn()
    {
        Assert.Equal("processing", OutboxMessageStatus.Processing);
        Assert.True(OutboxMessageStatus.Processing.Length <= 20);
    }

    // Provider idempotency keys

    [Fact]
    public void OutsideADispatch_ThereIsNoMessageAndNoIdempotencyKey()
    {
        var context = new OutboxDispatchContext();

        Assert.Null(context.CurrentMessageId);
        Assert.Null(context.NextIdempotencyKey());
    }

    [Fact]
    public void InsideADispatch_EachSendGetsAStableKeyDerivedFromTheMessageIdAndItsOrder()
    {
        var context = new OutboxDispatchContext();
        var messageId = Guid.NewGuid();

        context.Begin(messageId);

        Assert.Equal(messageId, context.CurrentMessageId);
        Assert.Equal($"outbox/{messageId:N}/1", context.NextIdempotencyKey());
        Assert.Equal($"outbox/{messageId:N}/2", context.NextIdempotencyKey());
    }

    [Fact]
    public void ARetryOfTheSameMessage_ProducesTheSameKeysAgain_AndEndClearsTheContext()
    {
        var context = new OutboxDispatchContext();
        var messageId = Guid.NewGuid();

        context.Begin(messageId);
        var firstAttempt = context.NextIdempotencyKey();
        context.End();
        Assert.Null(context.CurrentMessageId);
        Assert.Null(context.NextIdempotencyKey());

        context.Begin(messageId);
        var retry = context.NextIdempotencyKey();

        Assert.Equal(firstAttempt, retry);
        // Resend rejects keys outside 1-256 characters.
        Assert.InRange(retry!.Length, 1, 256);
    }
}
