using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Approval.OutboxHandlers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Approval;

public sealed class LeaveApprovalOutboxRegistrationTests
{
    [Theory]
    [InlineData(OutboxMessageTypes.LeaveRequestApproved)]
    [InlineData(OutboxMessageTypes.LeaveRequestRejected)]
    [InlineData(OutboxMessageTypes.LeaveInformationRequested)]
    public async Task NoOpHandler_CompletesWithoutThrowing_ForEachLeaveApprovalMessageType(string type)
    {
        var handler = new NoOpLeaveApprovalSideEffectOutboxHandler(type);

        Assert.Equal(type, handler.Type);
        await handler.HandleAsync("{}", CancellationToken.None);
    }
}
