using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.OutboxHandlers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class NoOpPositionOutboxHandlerTests
{
    [Theory]
    [InlineData(OutboxMessageTypes.PositionCreated)]
    [InlineData(OutboxMessageTypes.PositionUpdated)]
    [InlineData(OutboxMessageTypes.PositionArchived)]
    [InlineData(OutboxMessageTypes.PositionRestored)]
    public async Task HandleAsync_CompletesWithoutThrowing_ForEachPositionMessageType(string type)
    {
        var handler = new NoOpPositionOutboxHandler(type);

        Assert.Equal(type, handler.Type);
        await handler.HandleAsync("{}", CancellationToken.None);
    }
}
