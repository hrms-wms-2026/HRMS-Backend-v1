using ONEVO.Application.Common.Exceptions;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform;

/// <summary>The retry rule the claiming processor applies to a failed attempt (OutboxRetryPolicy.OnFailure).</summary>
public sealed class OutboxProcessorFailureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PermanentFailure_IsParkedFailedImmediately()
    {
        var outcome = OutboxRetryPolicy.OnFailure(0, new OutboxPermanentFailureException("connection gone"), Now);

        Assert.Equal(OutboxMessageStatus.Failed, outcome.Status);
        Assert.Equal(1, outcome.AttemptCount);
        Assert.Equal(Now, outcome.ProcessedAt);
        Assert.Null(outcome.NextAttemptAt);
    }

    [Fact]
    public void TransientFailure_IsRetriedWithBackoff()
    {
        var outcome = OutboxRetryPolicy.OnFailure(0, new HttpRequestException("503"), Now);

        Assert.Equal(OutboxMessageStatus.Pending, outcome.Status);
        Assert.True(outcome.NextAttemptAt > Now);
    }
}
