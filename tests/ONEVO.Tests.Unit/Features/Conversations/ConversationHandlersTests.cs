using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Commands.AddConversationParticipants;
using ONEVO.Application.Features.Conversations.Commands.MarkConversationRead;
using ONEVO.Application.Features.Conversations.Commands.OpenConversation;
using ONEVO.Application.Features.Conversations.Commands.PostConversationMessage;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.DTOs;
using ONEVO.Application.Features.Conversations.Queries.ListConversationMessages;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.Conversations.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Conversations;

public class ConversationHandlersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Approver = Guid.NewGuid();
    private static readonly Guid Third = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();
    private static readonly Guid Inactive = Guid.NewGuid();
    private static readonly ItemRef Subject = new("work", "approval", Guid.NewGuid());

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IMilestoneMembershipCoordinator> _membership = new();
    private readonly Mock<IOutboxWriter> _outbox = new();
    private readonly Mock<IInboxRealtimePublisher> _realtime = new();
    private readonly FakeSubjectResolver _resolver = new();
    private readonly InMemoryConversationRepository _repo = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly List<WorkNotificationPayload> _notifications = new();

    public ConversationHandlersTests()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _identity.Setup(x => x.ResolveIdentitiesByEmployeeIdAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<Guid> ids, CancellationToken _) => ids.Distinct().ToDictionary(id => id, id => new EmployeeIdentityDto(NameOf(id), null)));
        _identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<Guid> ids, CancellationToken _) => ids.Distinct().ToDictionary(id => id, NameOf));
        foreach (var id in new[] { Requester, Approver, Third, Stranger })
            _membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Employee { Id = id, TenantId = TenantId, UserId = Guid.NewGuid() });
        _outbox.Setup(x => x.EnqueueAsync(It.IsAny<string>(), It.IsAny<WorkNotificationPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((string _, WorkNotificationPayload p, Guid? _, CancellationToken _) => _notifications.Add(p))
            .Returns(Task.CompletedTask);
        _resolver.Subjects[Subject] = new ConversationSubject(new[] { Requester, Approver }, "Edit task \"Audit events\"", ProjectId);
        Caller(Requester);
    }

    private static string NameOf(Guid id)
        => id == Requester ? "Bala" : id == Approver ? "Anu" : id == Third ? "Priya" : id == Stranger ? "Chen" : "Someone";

    private void Caller(Guid employeeId)
        => _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(employeeId);

    private ConversationAccess Access() => new(new IConversationSubjectResolver[] { _resolver }, _repo);
    private ConversationResponseBuilder Builder() => new(_identity.Object, _repo);
    private ConversationNotifier Notifier() => new(_membership.Object, _outbox.Object);

    private OpenConversationCommandHandler Open() => new(_currentUser.Object, _identity.Object, Access(), _repo, Builder());
    private PostConversationMessageCommandHandler Post() => new(
        _currentUser.Object, _identity.Object, Access(), _repo, _uow, Notifier(), Builder(), _realtime.Object,
        NullLogger<PostConversationMessageCommandHandler>.Instance);
    private AddConversationParticipantsCommandHandler Add() => new(
        _currentUser.Object, _identity.Object, Access(), _repo, _uow, _membership.Object, Notifier(), Builder(), _realtime.Object,
        NullLogger<AddConversationParticipantsCommandHandler>.Instance);
    private MarkConversationReadCommandHandler MarkRead() => new(_currentUser.Object, _identity.Object, Access(), _repo, _uow);
    private ListConversationMessagesQueryHandler List() => new(_currentUser.Object, _identity.Object, Access(), _repo, Builder());
    private ConversationUnreadCounts Unread() => new(_repo);

    private async Task<ConversationResponse> OpenAs(Guid employeeId)
    {
        Caller(employeeId);
        var result = await Open().Handle(new OpenConversationCommand(Subject.Source, Subject.Kind, Subject.Id), default);
        result.IsSuccess.Should().BeTrue(result.Error);
        return result.Value!;
    }

    // ---- Open -------------------------------------------------------------------------------

    [Fact]
    public async Task Base_participant_opens_and_the_conversation_is_created_with_both_base_members()
    {
        var conversation = await OpenAs(Requester);

        _repo.Conversations.Should().ContainSingle(c => c.TenantId == TenantId && c.SubjectSource == "work"
            && c.SubjectKind == "approval" && c.SubjectId == Subject.Id);
        conversation.Title.Should().Be("Edit task \"Audit events\"");
        conversation.ProjectId.Should().Be(ProjectId);
        conversation.Participants.Select(p => (p.EmployeeId, p.Name, p.JoinReason)).Should().BeEquivalentTo(new[]
        {
            (Requester, "Bala", ConversationJoinReasons.Base), (Approver, "Anu", ConversationJoinReasons.Base)
        });
        conversation.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Opening_again_is_idempotent()
    {
        var first = await OpenAs(Requester);
        var second = await OpenAs(Approver);

        second.Id.Should().Be(first.Id);
        _repo.Conversations.Should().HaveCount(1);
        _repo.Participants.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_stranger_cannot_open_and_nothing_is_created()
    {
        Caller(Stranger);
        var result = await Open().Handle(new OpenConversationCommand(Subject.Source, Subject.Kind, Subject.Id), default);

        result.StatusCode.Should().Be(404);
        _repo.Conversations.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_source_or_missing_subject_fails_closed()
    {
        (await Open().Handle(new OpenConversationCommand("leave", "request", Subject.Id), default)).StatusCode.Should().Be(404);
        (await Open().Handle(new OpenConversationCommand("work", "approval", Guid.NewGuid()), default)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task A_caller_without_an_employee_record_is_forbidden()
    {
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        (await Open().Handle(new OpenConversationCommand(Subject.Source, Subject.Kind, Subject.Id), default)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Base_participants_are_resolved_at_read_time_so_a_transfer_moves_the_chat()
    {
        var conversation = await OpenAs(Approver);
        _resolver.Subjects[Subject] = new ConversationSubject(new[] { Requester, Third }, "Edit task \"Audit events\"", ProjectId);

        Caller(Approver);
        (await Open().Handle(new OpenConversationCommand(Subject.Source, Subject.Kind, Subject.Id), default)).StatusCode.Should().Be(404);
        (await List().Handle(new ListConversationMessagesQuery(conversation.Id, null), default)).StatusCode.Should().Be(404);

        var reopened = await OpenAs(Third);
        reopened.Participants.Select(p => p.EmployeeId).Should().BeEquivalentTo(new[] { Requester, Third });
    }

    // ---- Post -------------------------------------------------------------------------------

    [Fact]
    public async Task Posting_saves_the_message_bumps_last_message_and_publishes_after_save()
    {
        var conversation = await OpenAs(Requester);
        _realtime.Setup(x => x.MessageCreatedAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), conversation.Id, It.IsAny<ConversationMessageResponse>(), It.IsAny<CancellationToken>()))
            .Callback(() => _uow.SaveCallCount.Should().BeGreaterThan(0))
            .Returns(Task.CompletedTask);

        var result = await Post().Handle(new PostConversationMessageCommand(conversation.Id, "  <p>Can we keep the date?</p>  ", null), default);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.BodyHtml.Should().Be("<p>Can we keep the date?</p>");
        result.Value.AuthorEmployeeId.Should().Be(Requester);
        result.Value.AuthorName.Should().Be("Bala");
        result.Value.Kind.Should().Be(ConversationMessageKinds.Text);
        _repo.Messages.Should().ContainSingle();
        _repo.Conversations.Single().LastMessageAt.Should().NotBeNull();
        _realtime.Verify(x => x.MessageCreatedAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { Requester, Approver }.OrderBy(i => i))), conversation.Id, It.Is<ConversationMessageResponse>(m => m.Id == result.Value.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p> </p><div><br></div>")]
    [InlineData("<p>&nbsp;</p>")]
    public async Task An_empty_message_is_rejected(string body)
    {
        var conversation = await OpenAs(Requester);
        var result = await Post().Handle(new PostConversationMessageCommand(conversation.Id, body, null), default);

        result.StatusCode.Should().Be(400);
        _repo.Messages.Should().BeEmpty();
    }

    [Theory]
    [InlineData("<p>hi</p><script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<p ONCLICK = \"x()\">hi</p>")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<a href=' JavaScript:alert(1)'>x</a>")]
    [InlineData("<p>see</p><iframe src=\"https://evil\"></iframe>")]
    [InlineData("<p>see</p><object data=x></object>")]
    [InlineData("<p>see</p><svg><use href=x/></svg>")]
    [InlineData("<a href=\"java&#115;cript:alert(1)\">x</a>")]
    [InlineData("<a href=\"java\tscript:alert(1)\">x</a>")]
    [InlineData("<a href=\"data:text/html;base64,PHNjcmlwdD4=\">x</a>")]
    public async Task Active_content_is_rejected(string body)
    {
        var conversation = await OpenAs(Requester);
        var result = await Post().Handle(new PostConversationMessageCommand(conversation.Id, body, null), default);

        result.StatusCode.Should().Be(400);
        _repo.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Ordinary_formatting_and_mentions_are_accepted()
    {
        var conversation = await OpenAs(Requester);
        var body = "<div><b>Bold</b> <span class=\"mention\" data-employee-id=\"x\" contenteditable=\"false\">@Anu</span> "
            + "<a href=\"https://example.com\" target=\"_blank\">link</a> <font color=\"#e03131\">red</font> "
            + "<ul><li>one</li></ul><code>on=1</code> the word onerror and javascript: as text</div>";

        (await Post().Handle(new PostConversationMessageCommand(conversation.Id, body, null), default)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task An_image_only_message_is_not_empty()
    {
        var conversation = await OpenAs(Requester);
        (await Post().Handle(new PostConversationMessageCommand(conversation.Id, "<img src=\"/files/1\">", null), default))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_message_over_the_limit_is_rejected()
    {
        var conversation = await OpenAs(Requester);
        var body = "<p>" + new string('a', 20_000) + "</p>";
        (await Post().Handle(new PostConversationMessageCommand(conversation.Id, body, null), default)).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Mentioning_a_non_participant_is_rejected_with_422()
    {
        var conversation = await OpenAs(Requester);

        var result = await Post().Handle(new PostConversationMessageCommand(conversation.Id, "<p>hi</p>", new[] { Stranger }), default);

        result.StatusCode.Should().Be(422);
        _repo.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Mentioned_participants_are_notified_but_not_the_author()
    {
        var conversation = await OpenAs(Requester);

        var result = await Post().Handle(new PostConversationMessageCommand(conversation.Id, "<p>hi</p>", new[] { Approver, Requester, Approver }), default);

        result.Value!.MentionEmployeeIds.Should().BeEquivalentTo(new[] { Approver, Requester });
        _repo.Mentions.Should().HaveCount(2);
        _notifications.Should().ContainSingle();
        var n = _notifications.Single();
        n.TemplateCode.Should().Be(ConversationNotifier.MentionedTemplate);
        n.RelatedEntityType.Should().Be("inbox:work:approval");
        n.RelatedEntityId.Should().Be(Subject.Id);
        n.Placeholders["actorName"].Should().Be("Bala");
        n.Placeholders["itemTitle"].Should().Be("Edit task \"Audit events\"");
    }

    [Fact]
    public async Task A_stranger_cannot_post_into_a_conversation()
    {
        var conversation = await OpenAs(Requester);
        Caller(Stranger);

        (await Post().Handle(new PostConversationMessageCommand(conversation.Id, "<p>hi</p>", null), default)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task A_conversation_of_another_tenant_is_not_found()
    {
        var foreign = new Conversation { Id = Guid.NewGuid(), TenantId = OtherTenantId, SubjectSource = "work", SubjectKind = "approval", SubjectId = Subject.Id };
        _repo.Conversations.Add(foreign);

        (await Post().Handle(new PostConversationMessageCommand(foreign.Id, "<p>hi</p>", null), default)).StatusCode.Should().Be(404);
        (await List().Handle(new ListConversationMessagesQuery(foreign.Id, null), default)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task A_realtime_failure_does_not_fail_the_post()
    {
        var conversation = await OpenAs(Requester);
        _realtime.Setup(x => x.MessageCreatedAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>(), It.IsAny<ConversationMessageResponse>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub down"));

        (await Post().Handle(new PostConversationMessageCommand(conversation.Id, "<p>hi</p>", null), default)).IsSuccess.Should().BeTrue();
    }

    // ---- Add participants -------------------------------------------------------------------

    [Fact]
    public async Task Adding_a_member_writes_a_row_a_system_message_a_notification_and_realtime_events()
    {
        var conversation = await OpenAs(Requester);

        var result = await Add().Handle(new AddConversationParticipantsCommand(conversation.Id, new[] { Third }), default);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Should().ContainSingle(p => p.EmployeeId == Third && p.JoinReason == ConversationJoinReasons.Added
            && p.AddedByEmployeeId == Requester && p.Name == "Priya");
        _repo.Participants.Should().ContainSingle(p => p.EmployeeId == Third && p.JoinReason == ConversationJoinReasons.Added);
        var system = _repo.Messages.Should().ContainSingle().Subject;
        system.Kind.Should().Be(ConversationMessageKinds.System);
        system.AuthorEmployeeId.Should().BeNull();
        system.SystemEventJson.Should().Contain("participant_added").And.Contain(Requester.ToString()).And.Contain(Third.ToString());
        _notifications.Should().ContainSingle(n => n.TemplateCode == ConversationNotifier.MemberAddedTemplate);
        _realtime.Verify(x => x.ParticipantAddedAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(Third) && ids.Contains(Approver)), conversation.Id, It.Is<ConversationParticipantResponse>(p => p.EmployeeId == Third), It.IsAny<CancellationToken>()), Times.Once);
        _realtime.Verify(x => x.MessageCreatedAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(Third)), conversation.Id, It.Is<ConversationMessageResponse>(m => m.Kind == ConversationMessageKinds.System), It.IsAny<CancellationToken>()), Times.Once);
        _realtime.Verify(x => x.InboxChangedAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { Third })), Subject, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Adding_skips_base_members_already_added_members_and_duplicates()
    {
        var conversation = await OpenAs(Requester);
        await Add().Handle(new AddConversationParticipantsCommand(conversation.Id, new[] { Third }), default);
        _notifications.Clear();

        var result = await Add().Handle(new AddConversationParticipantsCommand(conversation.Id, new[] { Approver, Third, Requester }), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().BeEmpty();
        _repo.Participants.Should().HaveCount(3);
        _repo.Messages.Should().HaveCount(1);
        _notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task An_added_member_can_read_chat_and_add_others()
    {
        var conversation = await OpenAs(Requester);
        await Add().Handle(new AddConversationParticipantsCommand(conversation.Id, new[] { Third }), default);

        var opened = await OpenAs(Third);
        opened.Participants.Should().Contain(p => p.EmployeeId == Third && p.JoinReason == ConversationJoinReasons.Added);
        (await Post().Handle(new PostConversationMessageCommand(conversation.Id, "<p>joining</p>", new[] { Requester }), default)).IsSuccess.Should().BeTrue();
        (await Add().Handle(new AddConversationParticipantsCommand(conversation.Id, new[] { Stranger }), default)).Value.Should().ContainSingle();
    }

    [Fact]
    public async Task Adding_an_inactive_or_unknown_employee_is_rejected_with_422()
    {
        var conversation = await OpenAs(Requester);

        var result = await Add().Handle(new AddConversationParticipantsCommand(conversation.Id, new[] { Third, Inactive }), default);

        result.StatusCode.Should().Be(422);
        _repo.Participants.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_stranger_cannot_add_members()
    {
        var conversation = await OpenAs(Requester);
        Caller(Stranger);

        (await Add().Handle(new AddConversationParticipantsCommand(conversation.Id, new[] { Stranger }), default)).StatusCode.Should().Be(404);
    }

    // ---- Read state ---------------------------------------------------------------------------

    [Fact]
    public async Task Unread_counts_other_peoples_messages_until_marked_read()
    {
        var conversation = await OpenAs(Requester);
        await Post().Handle(new PostConversationMessageCommand(conversation.Id, "<p>one</p>", null), default);
        await Post().Handle(new PostConversationMessageCommand(conversation.Id, "<p>two</p>", null), default);

        (await Unread().GetAsync(TenantId, Approver, new[] { Subject }, default)).Should().Contain(Subject, 2);
        (await Unread().GetAsync(TenantId, Requester, new[] { Subject }, default)).Should().NotContainKey(Subject);

        Caller(Approver);
        (await MarkRead().Handle(new MarkConversationReadCommand(conversation.Id), default)).IsSuccess.Should().BeTrue();

        (await Unread().GetAsync(TenantId, Approver, new[] { Subject }, default)).Should().NotContainKey(Subject);
    }

    [Fact]
    public async Task A_stranger_cannot_mark_read()
    {
        var conversation = await OpenAs(Requester);
        Caller(Stranger);
        (await MarkRead().Handle(new MarkConversationReadCommand(conversation.Id), default)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Messages_page_backwards_oldest_first_with_mentions()
    {
        var conversation = await OpenAs(Requester);
        var start = DateTimeOffset.UtcNow.AddMinutes(-10);
        for (var i = 0; i < 5; i++)
            _repo.Messages.Add(new ConversationMessage
            {
                Id = Guid.NewGuid(), TenantId = TenantId, ConversationId = conversation.Id, AuthorEmployeeId = Approver,
                Kind = ConversationMessageKinds.Text, BodyHtml = $"<p>{i}</p>", CreatedAt = start.AddMinutes(i)
            });

        var newest = await List().Handle(new ListConversationMessagesQuery(conversation.Id, null, 2), default);
        newest.Value!.Items.Select(m => m.BodyHtml).Should().Equal("<p>3</p>", "<p>4</p>");
        newest.Value.HasMore.Should().BeTrue();

        var older = await List().Handle(new ListConversationMessagesQuery(conversation.Id, newest.Value.Items[0].CreatedAt, 10), default);
        older.Value!.Items.Select(m => m.BodyHtml).Should().Equal("<p>0</p>", "<p>1</p>", "<p>2</p>");
        older.Value.HasMore.Should().BeFalse();
    }

    private sealed class FakeSubjectResolver : IConversationSubjectResolver
    {
        public Dictionary<ItemRef, ConversationSubject> Subjects { get; } = new();
        public string Source => "work";

        public Task<ConversationSubject?> ResolveAsync(Guid tenantId, ItemRef subject, CancellationToken ct)
            => Task.FromResult(tenantId == TenantId ? Subjects.GetValueOrDefault(subject) : null);
    }
}
