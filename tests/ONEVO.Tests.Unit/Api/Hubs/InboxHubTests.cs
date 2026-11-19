using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Api.Hubs;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Api.Hubs;

public class InboxHubTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static (InboxHub Hub, Mock<IGroupManager> Groups, Mock<HubCallerContext> Context) CreateHub(params Claim[] claims)
    {
        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.ConnectionId).Returns("conn-1");
        context.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, "TenantScheme")));
        var groups = new Mock<IGroupManager>();
        var hub = new InboxHub { Context = context.Object, Groups = groups.Object };
        return (hub, groups, context);
    }

    [Fact]
    public async Task A_connection_joins_only_its_own_tenant_scoped_user_group()
    {
        var (hub, groups, _) = CreateHub(new Claim("tenant_id", TenantId.ToString()), new Claim(ClaimTypes.NameIdentifier, UserId.ToString()));

        await hub.OnConnectedAsync();

        groups.Verify(g => g.AddToGroupAsync("conn-1", $"inbox:{TenantId:D}:{UserId:D}", It.IsAny<CancellationToken>()), Times.Once);
        groups.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_connection_without_tenant_or_user_claims_is_aborted()
    {
        var (hub, groups, context) = CreateHub(new Claim(ClaimTypes.NameIdentifier, UserId.ToString()));

        await hub.OnConnectedAsync();

        context.Verify(c => c.Abort(), Times.Once);
        groups.VerifyNoOtherCalls();
    }
}

public class SignalRInboxRealtimePublisherTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid AliceUser = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid BobUser = Guid.NewGuid();
    private static readonly Guid Inactive = Guid.NewGuid();

    private readonly Mock<IHubContext<InboxHub>> _hub = new();
    private readonly Mock<IHubClients> _clients = new();
    private readonly Mock<IClientProxy> _proxy = new();
    private readonly Mock<IMilestoneMembershipCoordinator> _membership = new();
    private IReadOnlyList<string>? _groups;

    public SignalRInboxRealtimePublisherTests()
    {
        _hub.SetupGet(h => h.Clients).Returns(_clients.Object);
        _clients.Setup(c => c.Groups(It.IsAny<IReadOnlyList<string>>()))
            .Callback((IReadOnlyList<string> groups) => _groups = groups)
            .Returns(_proxy.Object);
        _membership.Setup(m => m.GetActiveAssigneeAsync(TenantId, Alice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Alice, UserId = AliceUser });
        _membership.Setup(m => m.GetActiveAssigneeAsync(TenantId, Bob, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Bob, UserId = BobUser });
    }

    private SignalRInboxRealtimePublisher Publisher()
        => new(_hub.Object, _membership.Object, NullLogger<SignalRInboxRealtimePublisher>.Instance);

    [Fact]
    public async Task MessageCreated_goes_to_each_active_recipients_user_group_once()
    {
        var conversationId = Guid.NewGuid();
        var message = new ConversationMessageResponse(Guid.NewGuid(), conversationId, Alice, "Alice", null, "text", "<p>hi</p>", null, [], DateTimeOffset.UtcNow);

        await Publisher().MessageCreatedAsync(TenantId, new[] { Alice, Bob, Alice, Inactive }, conversationId, message);

        _groups.Should().BeEquivalentTo($"inbox:{TenantId:D}:{AliceUser:D}", $"inbox:{TenantId:D}:{BobUser:D}");
        _proxy.Verify(p => p.SendCoreAsync("messageCreated",
            It.Is<object?[]>(args => args.Length == 1 && args[0]!.ToString()!.Contains(conversationId.ToString())),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InboxChanged_carries_the_item_ref()
    {
        var item = new ItemRef("work", "approval", Guid.NewGuid());

        await Publisher().InboxChangedAsync(TenantId, new[] { Bob }, item);

        _groups.Should().Equal($"inbox:{TenantId:D}:{BobUser:D}");
        _proxy.Verify(p => p.SendCoreAsync("inboxChanged",
            It.Is<object?[]>(args => args[0]!.ToString()!.Contains(item.Id.ToString()) && args[0]!.ToString()!.Contains("approval")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Nothing_is_sent_when_no_recipient_is_active()
    {
        await Publisher().ParticipantAddedAsync(TenantId, new[] { Inactive }, Guid.NewGuid(),
            new ConversationParticipantResponse(Inactive, "X", null, "added", Alice, DateTimeOffset.UtcNow));

        _clients.Verify(c => c.Groups(It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }

    [Fact]
    public async Task A_hub_failure_is_swallowed()
    {
        _proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("backplane down"));

        var act = () => Publisher().InboxChangedAsync(TenantId, new[] { Alice }, new ItemRef("work", "approval", Guid.NewGuid()));

        await act.Should().NotThrowAsync();
    }
}
