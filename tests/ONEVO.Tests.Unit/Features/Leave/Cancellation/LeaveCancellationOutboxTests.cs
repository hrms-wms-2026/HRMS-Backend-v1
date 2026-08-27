using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Cancellation.Outbox;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Cancellation;

public sealed class LeaveCancellationOutboxTests
{
    [Fact]
    public void LeaveRequestCancelled_UsesStableWireValue()
    {
        Assert.Equal("leave_request_cancelled", OutboxMessageTypes.LeaveRequestCancelled);
    }

    [Fact]
    public async Task NoOpHandler_AdvertisesCancelledTypeAndCompletes()
    {
        var handler = new NoOpLeaveCancellationSideEffectOutboxHandler();
        Assert.Equal(OutboxMessageTypes.LeaveRequestCancelled, handler.Type);
        await handler.HandleAsync("{}", CancellationToken.None);
    }
}
