using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Inbox.OutboxHandlers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Inbox;

public class InboxChangedOutboxHandlerTests
{
    private readonly Mock<IInboxRealtimePublisher> _realtime = new();

    [Fact]
    public async Task Publishes_inbox_changed_for_the_payload_people_and_item()
    {
        var tenant = Guid.NewGuid();
        var people = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var id = Guid.NewGuid();
        var handler = new InboxChangedOutboxHandler(_realtime.Object);

        await handler.HandleAsync(JsonSerializer.Serialize(new InboxChangedPayload(tenant, people, "work", "approval", id)), default);

        handler.Type.Should().Be(OutboxMessageTypes.InboxChanged);
        _realtime.Verify(r => r.InboxChangedAsync(tenant, It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(people)),
            new ItemRef("work", "approval", id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_empty_payload_fails_so_the_outbox_retries_and_parks_it()
    {
        var act = () => new InboxChangedOutboxHandler(_realtime.Object).HandleAsync("null", default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
