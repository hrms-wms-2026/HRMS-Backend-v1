using ONEVO.Application.Common.Exceptions;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform;

public sealed class OutboxProcessorFailureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PermanentFailure_IsParkedFailedImmediately()
    {
        var message = new OutboxMessage { Status = OutboxMessageStatus.Pending, AttemptCount = 0 };

        OutboxProcessor.ApplyFailure(message, new OutboxPermanentFailureException("connection gone"), Now);

        Assert.Equal(OutboxMessageStatus.Failed, message.Status);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal(Now, message.ProcessedAt);
    }

    [Fact]
    public void TransientFailure_IsRetriedWithBackoff()
    {
        var message = new OutboxMessage { Status = OutboxMessageStatus.Pending, AttemptCount = 0 };

        OutboxProcessor.ApplyFailure(message, new HttpRequestException("503"), Now);

        Assert.Equal(OutboxMessageStatus.Pending, message.Status);
        Assert.True(message.NextAttemptAt > Now);
    }
}
