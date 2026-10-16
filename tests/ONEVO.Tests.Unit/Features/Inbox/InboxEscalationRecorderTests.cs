using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Conversations.Entities;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Tests.Unit.Fakes;
using ONEVO.Tests.Unit.Features.Conversations;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Inbox;

public class InboxEscalationRecorderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid Hr = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly ItemRef Item = new("monitoring", "exception_case", Guid.NewGuid());

    private readonly InMemoryInboxDecisionJournal _journal = new();
    private readonly InMemoryConversationRepository _conversations = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public InboxEscalationRecorderTests()
    {
        _identity.Setup(i => i.ResolveIdentitiesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, EmployeeIdentityDto> { [Hr] = new("Hema HR", null) });
    }

    private InboxEscalationRecorder Recorder()
        => new(_journal, _conversations, _identity.Object, _uow.Object, new FakeDateTimeProvider { UtcNow = Now });

    [Fact]
    public async Task An_escalation_is_journaled_its_reviewers_join_the_chat_as_viewers_and_the_chat_says_who_it_went_to()
    {
        var affected = await Recorder().RecordAsync(TenantId, new EscalationRecord(Item,
            new EscalationTarget.Employees([new EscalationRecipient(Guid.NewGuid(), Hr), new EscalationRecipient(Guid.NewGuid(), null)]),
            Manager, "  please look  ", [Manager]), default);

        affected.Should().BeEquivalentTo([Manager, Hr]);
        var row = _journal.Entries.Should().ContainSingle().Subject;
        (row.Action, row.ActorEmployeeId, row.Comment, row.ItemId).Should().Be((InboxJournalActions.Escalate, Manager, "please look", Item.Id));
        _conversations.Participants.Should().ContainSingle(p => p.EmployeeId == Hr && p.JoinReason == ConversationJoinReasons.Viewer);
        var message = _conversations.Messages.Should().ContainSingle().Subject;
        message.Kind.Should().Be(ConversationMessageKinds.System);
        message.BodyHtml.Should().Be("Escalated to Hema HR");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Nobody_above_is_logged_only_with_no_viewers()
    {
        var affected = await Recorder().RecordAsync(TenantId, new EscalationRecord(Item,
            new EscalationTarget.LogOnly("No one above - logged."), null, null, [Manager]), default);

        affected.Should().Equal(Manager);
        _journal.Entries.Single().ActorEmployeeId.Should().Be(Guid.Empty);
        _journal.Entries.Single().AfterJson.Should().Contain("No one above");
        _conversations.Participants.Should().BeEmpty();
        _conversations.Messages.Single().BodyHtml.Should().Be(InboxEscalationRecorder.LoggedOnlyBody);
    }
}
