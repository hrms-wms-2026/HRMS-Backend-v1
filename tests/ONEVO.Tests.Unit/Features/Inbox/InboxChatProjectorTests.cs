using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Inbox.Chats;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Conversations.Entities;
using ONEVO.Tests.Unit.Features.Conversations;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Inbox;

/// <summary>Case cards in people chats: one card per item in the 1-on-1 chat of its person and reviewer, a resolved
/// line when it closes, and the case group when a later approver takes over.</summary>
public class InboxChatProjectorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Priya = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid Hr = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryConversationRepository _repo = new();
    private readonly Mock<IConversationAccess> _access = new();
    private readonly InboxChatProjector _projector;

    public InboxChatProjectorTests()
    {
        var clock = new Mock<IDateTimeProvider>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(i => i.ResolveIdentitiesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<Guid> ids, CancellationToken _) =>
                ids.ToDictionary(id => id, id => new EmployeeIdentityDto(id == Hr ? "Anita Hr" : "Someone", null)));
        _access.Setup(a => a.ResolveSubjectAsync(TenantId, It.IsAny<ItemRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationSubject([Priya, Manager, Hr], "Annual leave - Priya", null));
        var people = new PeopleChatService(_repo, clock.Object);
        _projector = new InboxChatProjector(_repo, people, new CaseGroupService(_repo, _access.Object, identity.Object, clock.Object));
    }

    private static InboxItem Leave(Guid id, string direction, Guid? holder, bool pending, string status = "pending")
        => new("leave", "request", id, InboxCategories.Request, "leave", "Annual leave - Priya", null, status, direction, null,
            "legal_entity", null, null, new InboxPerson(Priya, "Priya"), holder is { } h ? new InboxPerson(h, "Holder") : null,
            Now.AddDays(-2), Now.AddDays(-1), [], 0, pending, new Dictionary<string, IReadOnlyList<string>>());

    private Task<IReadOnlyDictionary<ItemRef, Guid>> Project(Guid caller, params InboxItem[] items)
        => _projector.ProjectAsync(TenantId, caller, items, [], CancellationToken.None);

    private List<ConversationMessage> Cards => _repo.Messages.Where(m => m.Kind == ConversationMessageKinds.Case).ToList();

    [Fact]
    public async Task A_new_request_lands_as_one_card_in_the_pair_chat_whichever_side_loads_first()
    {
        var id = Guid.NewGuid();

        var fromManager = await Project(Manager, Leave(id, InboxDirections.Received, Manager, pending: true));
        var fromPriya = await Project(Priya, Leave(id, InboxDirections.Sent, Manager, pending: true));
        await Project(Manager, Leave(id, InboxDirections.Received, Manager, pending: true));

        Cards.Should().ContainSingle();
        var chat = _repo.Conversations.Single();
        chat.SubjectSource.Should().Be(ConversationSources.Direct);
        _repo.Participants.Where(p => p.ConversationId == chat.Id).Select(p => p.EmployeeId).Should().BeEquivalentTo([Priya, Manager]);
        fromManager[new ItemRef("leave", "request", id)].Should().Be(chat.Id);
        fromPriya[new ItemRef("leave", "request", id)].Should().Be(chat.Id);
        Cards[0].CreatedAt.Should().Be(Now.AddDays(-2), "the card sits where the request was made");
    }

    [Fact]
    public async Task A_second_case_of_the_same_pair_continues_the_same_chat()
    {
        await Project(Manager, Leave(Guid.NewGuid(), InboxDirections.Received, Manager, pending: false, status: "approved"));
        await Project(Manager, Leave(Guid.NewGuid(), InboxDirections.Received, Manager, pending: true));

        _repo.Conversations.Should().ContainSingle();
        Cards.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_decided_request_gets_a_resolved_line_once()
    {
        var id = Guid.NewGuid();
        await Project(Manager, Leave(id, InboxDirections.Received, Manager, pending: true));
        await Project(Manager, Leave(id, InboxDirections.Received, Manager, pending: false, status: "approved"));
        await Project(Priya, Leave(id, InboxDirections.Sent, Manager, pending: false, status: "approved"));

        var resolved = _repo.Messages.Where(m => m.SystemEventJson?.StartsWith("{\"type\":\"case_resolved\"") == true).ToList();
        resolved.Should().ContainSingle();
        resolved[0].BodyHtml.Should().Be("Annual leave approved");
    }

    [Fact]
    public async Task The_resolved_line_names_who_decided_from_the_journal_with_the_note()
    {
        var id = Guid.NewGuid();
        var journal = new InMemoryInboxDecisionJournal();
        await journal.RecordAsync(new ONEVO.Domain.Features.Inbox.Entities.InboxDecisionJournalEntry
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Source = "leave", Kind = "request", ItemId = id, Action = "reject",
            ActorEmployeeId = Hr, Comment = "Team is short", DecidedAt = Now, CreatedAt = Now,
        });
        var clock = new Mock<IDateTimeProvider>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(i => i.ResolveIdentitiesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<Guid> ids, CancellationToken _) => ids.ToDictionary(x => x, x => new EmployeeIdentityDto("Anita Hr", null)));
        var projector = new InboxChatProjector(_repo, new PeopleChatService(_repo, clock.Object),
            new CaseGroupService(_repo, _access.Object, identity.Object, clock.Object), journal, identity.Object);

        await projector.ProjectAsync(TenantId, Manager, [Leave(id, InboxDirections.Received, Manager, pending: false, status: "rejected")], [],
            CancellationToken.None);

        var line = _repo.Messages.Single(m => m.SystemEventJson?.StartsWith("{\"type\":\"case_resolved\"") == true);
        line.BodyHtml.Should().Be("Annual leave rejected by Anita Hr");
        line.SystemEventJson.Should().Contain("\"byName\":\"Anita Hr\"").And.Contain("\"comment\":\"Team is short\"");
    }

    [Fact]
    public async Task A_step_one_approver_seeing_an_open_two_step_request_adds_no_resolved_line()
    {
        var id = Guid.NewGuid();
        await Project(Manager, Leave(id, InboxDirections.Received, Manager, pending: true));

        // Step 1 approved: not pending on the manager any more, but the request itself is still pending (step 2).
        await Project(Manager, Leave(id, InboxDirections.Received, Hr, pending: false, status: "pending"));

        _repo.Messages.Should().NotContain(m => m.SystemEventJson != null && m.SystemEventJson.StartsWith("{\"type\":\"case_resolved\""));
    }

    [Fact]
    public async Task An_undone_decision_takes_the_resolved_line_away_again()
    {
        var id = Guid.NewGuid();
        await Project(Manager, Leave(id, InboxDirections.Received, Manager, pending: false, status: "approved"));
        _repo.Messages.Should().Contain(m => m.BodyHtml == "Annual leave approved");

        await Project(Manager, Leave(id, InboxDirections.Received, Manager, pending: true));

        _repo.Messages.Should().NotContain(m => m.BodyHtml == "Annual leave approved");
        Cards.Should().ContainSingle();
    }

    [Fact]
    public async Task An_escalation_moves_the_case_to_its_group_with_an_escalation_line()
    {
        var id = Guid.NewGuid();
        var item = new ItemRef("leave", "request", id);
        await Project(Manager, Leave(id, InboxDirections.Received, Manager, pending: true));
        var dm = _repo.Conversations.Single();

        var clock = new Mock<IDateTimeProvider>();
        clock.Setup(c => c.UtcNow).Returns(Now.AddMinutes(5));
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(i => i.ResolveIdentitiesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<Guid> ids, CancellationToken _) => ids.ToDictionary(x => x, x => new EmployeeIdentityDto("Anita Hr", null)));
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(ICaseGroupService)))
            .Returns(new CaseGroupService(_repo, _access.Object, identity.Object, clock.Object));
        var recorder = new ONEVO.Application.Features.Inbox.Escalation.InboxEscalationRecorder(
            Mock.Of<IInboxDecisionJournal>(), _repo, identity.Object, Mock.Of<ONEVO.Application.Common.RepositoryInterfaces.IUnitOfWork>(),
            clock.Object, services.Object);

        await recorder.RecordAsync(TenantId, new ONEVO.Application.Features.Inbox.Escalation.EscalationRecord(
            item, new ONEVO.Application.Features.Inbox.Escalation.EscalationTarget.Employees([new(Guid.NewGuid(), Hr)]), Manager, null, [Manager]),
            CancellationToken.None);

        var group = _repo.Conversations.Single(c => c.SubjectSource == "leave");
        var lines = _repo.Messages.Where(m => m.ConversationId == group.Id).Select(m => m.BodyHtml).ToList();
        lines.Should().Contain(["Group started for Annual leave - Priya", "Escalated to Anita Hr"]);
        _repo.Messages.Should().Contain(m => m.ConversationId == group.Id && m.Kind == ConversationMessageKinds.Case);
        _repo.Messages.Should().Contain(m => m.ConversationId == dm.Id && m.BodyHtml.StartsWith("Continued in a group"));
        _repo.Participants.Where(p => p.ConversationId == group.Id).Select(p => p.EmployeeId).Should().Contain([Priya, Manager, Hr]);
    }

    [Fact]
    public async Task An_escalation_with_no_group_goes_to_the_requesters_1on1_chat_with_the_new_approver()
    {
        var id = Guid.NewGuid();
        var item = new ItemRef("leave", "request", id);
        await Project(Manager, Leave(id, InboxDirections.Received, Manager, pending: true));
        var managerDm = _repo.Conversations.Single();

        var clock = new Mock<IDateTimeProvider>();
        clock.Setup(c => c.UtcNow).Returns(Now.AddMinutes(5));
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(i => i.ResolveIdentitiesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<Guid> ids, CancellationToken _) =>
                ids.ToDictionary(x => x, x => new EmployeeIdentityDto(x == Hr ? "Anita Hr" : "Mark Manager", null)));
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(ICaseGroupService)))
            .Returns(new CaseGroupService(_repo, _access.Object, identity.Object, clock.Object, new PeopleChatService(_repo, clock.Object)));
        var recorder = new ONEVO.Application.Features.Inbox.Escalation.InboxEscalationRecorder(
            Mock.Of<IInboxDecisionJournal>(), _repo, identity.Object, Mock.Of<ONEVO.Application.Common.RepositoryInterfaces.IUnitOfWork>(),
            clock.Object, services.Object);

        await recorder.RecordAsync(TenantId, new ONEVO.Application.Features.Inbox.Escalation.EscalationRecord(
            item, new ONEVO.Application.Features.Inbox.Escalation.EscalationTarget.Employees([new(Guid.NewGuid(), Hr)]), Manager, "On leave",
            [Manager], SubjectEmployeeId: Priya), CancellationToken.None);

        _repo.Conversations.Should().NotContain(c => c.SubjectSource == "leave", "no group is started");
        var hrDm = _repo.Conversations.Single(c => c.Id != managerDm.Id);
        Cards.Should().Contain(c => c.ConversationId == hrDm.Id);
        _repo.Messages.Should().Contain(m => m.ConversationId == hrDm.Id && m.BodyHtml == "Escalated to Anita Hr by Mark Manager: On leave"
            && m.AuthorEmployeeId == null);
        _repo.Messages.Should().Contain(m => m.ConversationId == managerDm.Id && m.BodyHtml.StartsWith("Escalated to Anita Hr"));

        // HR's own chat-list load finds the card where it already is, and does not start a group either.
        var placed = await Project(Hr, Leave(id, InboxDirections.Received, Hr, pending: true));
        placed[item].Should().Be(hrDm.Id);
        _repo.Conversations.Should().NotContain(c => c.SubjectSource == "leave");
    }

    [Fact]
    public async Task The_next_approver_joins_the_case_group_instead_of_a_new_1on1_card()
    {
        var id = Guid.NewGuid();
        await Project(Manager, Leave(id, InboxDirections.Received, Manager, pending: true));
        var dm = _repo.Conversations.Single();

        var placed = await Project(Hr, Leave(id, InboxDirections.Received, Hr, pending: true));

        var group = _repo.Conversations.Single(c => c.SubjectSource == "leave");
        placed[new ItemRef("leave", "request", id)].Should().Be(group.Id);
        _repo.Messages.Where(m => m.ConversationId == group.Id).Select(m => m.BodyHtml)
            .Should().Contain(["Group started for Annual leave - Priya", "Next approver: Anita Hr"]);
        _repo.Messages.Should().Contain(m => m.ConversationId == dm.Id && m.BodyHtml.StartsWith("Continued in a group"));
        Cards.Where(c => c.ConversationId != group.Id).Should().ContainSingle("HR gets no 1-on-1 card of their own");
        _repo.Participants.Where(p => p.ConversationId == group.Id).Select(p => p.EmployeeId).Should().BeEquivalentTo([Priya, Manager, Hr]);

        // Repeats change nothing.
        await Project(Hr, Leave(id, InboxDirections.Received, Hr, pending: true));
        _repo.Messages.Count(m => m.BodyHtml == "Next approver: Anita Hr").Should().Be(1);
    }

    [Fact]
    public async Task The_badge_counts_unread_in_1on1_chats_groups_and_case_groups_but_not_hidden_item_chats()
    {
        Guid Chat(string source, Guid subjectId)
        {
            var c = new Conversation { Id = Guid.NewGuid(), TenantId = TenantId, SubjectSource = source, SubjectKind = source, SubjectId = subjectId };
            _repo.Conversations.Add(c);
            _repo.Participants.Add(new ConversationParticipant { Id = Guid.NewGuid(), TenantId = TenantId, ConversationId = c.Id, EmployeeId = Manager });
            return c.Id;
        }
        void Unread(Guid chat, int count, string? json = null)
        {
            for (var i = 0; i < count; i++)
                _repo.Messages.Add(new ConversationMessage
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, ConversationId = chat, AuthorEmployeeId = json is null ? Priya : null,
                    Kind = json is null ? ConversationMessageKinds.Text : ConversationMessageKinds.System, SystemEventJson = json, CreatedAt = Now,
                });
        }
        Unread(Chat(ConversationSources.Direct, Guid.NewGuid()), 2);
        Unread(Chat(ConversationSources.Group, Guid.NewGuid()), 1);
        Unread(Chat("leave", Guid.NewGuid()), 5); // an old per-item chat: not listed, not counted
        Unread(Chat("monitoring", Guid.NewGuid()), 1, "{\"type\":\"case_group\",\"title\":\"Face check\"}");

        var total = await new ConversationUnreadCounts(_repo).TotalInChatsAsync(TenantId, Manager, CancellationToken.None);

        total.Should().Be(4);
    }

    [Fact]
    public async Task A_forwarded_copy_elsewhere_does_not_make_the_reviewer_a_next_approver()
    {
        var id = Guid.NewGuid();
        var item = new ItemRef("leave", "request", id);
        // Someone forwarded the request into another chat before the reviewer opened their inbox.
        _repo.Messages.Add(new ConversationMessage
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ConversationId = Guid.NewGuid(), AuthorEmployeeId = Hr,
            Kind = ConversationMessageKinds.Case, SystemEventJson = CaseCards.CardJson(item, "Annual leave - Priya", forwarded: true), CreatedAt = Now,
        });

        await Project(Manager, Leave(id, InboxDirections.Received, Manager, pending: true));

        _repo.Conversations.Should().ContainSingle(c => c.SubjectSource == ConversationSources.Direct);
        _repo.Conversations.Should().NotContain(c => c.SubjectSource == "leave");
    }

    [Fact]
    public async Task A_favorite_comes_back_on_its_chat_row_for_that_person_only()
    {
        await Project(Manager, Leave(Guid.NewGuid(), InboxDirections.Received, Manager, pending: true));
        var chat = _repo.Conversations.Single();
        await _repo.SetFavoriteAsync(TenantId, Manager, chat.Id, favorite: true);

        var router = new Mock<ONEVO.Application.Features.Inbox.Services.IInboxRouter>();
        router.Setup(r => r.ListAsync(TenantId, It.IsAny<Guid>(), It.IsAny<InboxQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InboxPage([], 0, 1, 100));
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(i => i.ResolveIdentitiesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, EmployeeIdentityDto>());
        var service = new InboxChatService(router.Object, _projector, _repo, identity.Object);

        (await service.ListAsync(TenantId, Manager, CancellationToken.None)).Single().IsFavorite.Should().BeTrue();
        (await service.ListAsync(TenantId, Priya, CancellationToken.None)).Single().IsFavorite.Should().BeFalse();

        await _repo.SetFavoriteAsync(TenantId, Manager, chat.Id, favorite: false);
        (await service.ListAsync(TenantId, Manager, CancellationToken.None)).Single().IsFavorite.Should().BeFalse();
    }

    [Fact]
    public async Task Watchers_and_alerts_without_a_holder_place_nothing_from_the_subjects_side()
    {
        await Project(Priya, Leave(Guid.NewGuid(), InboxDirections.Info, holder: null, pending: true));
        await Project(Hr, Leave(Guid.NewGuid(), InboxDirections.Info, Manager, pending: true));

        _repo.Conversations.Should().BeEmpty();
    }
}
