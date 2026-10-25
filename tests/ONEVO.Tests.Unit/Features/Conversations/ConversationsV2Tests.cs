using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Commands.AddConversationParticipants;
using ONEVO.Application.Features.Conversations.Commands.DeleteConversationMessage;
using ONEVO.Application.Features.Conversations.Commands.EditConversationMessage;
using ONEVO.Application.Features.Conversations.Commands.LeaveConversation;
using ONEVO.Application.Features.Conversations.Commands.OpenConversation;
using ONEVO.Application.Features.Conversations.Commands.PostConversationMessage;
using ONEVO.Application.Features.Conversations.Commands.ReactToConversationMessage;
using ONEVO.Application.Features.Conversations.Commands.RemoveConversationParticipant;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.DTOs;
using ONEVO.Application.Features.Conversations.Queries.GetConversationAttachment;
using ONEVO.Application.Features.Conversations.Queries.ListConversationMessages;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.Conversations.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Conversations;

/// <summary>Conversations v2 (plan S3): remove/leave, edit/delete with an audit trail, attachments, viewers, tags.</summary>
public class ConversationsV2Tests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Approver = Guid.NewGuid();
    private static readonly Guid Subject = Guid.NewGuid();
    private static readonly Guid Third = Guid.NewGuid();
    private static readonly Guid Fourth = Guid.NewGuid();
    private static readonly Guid Hr = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();
    private static readonly ItemRef Item = new("monitoring", "exception_case", Guid.NewGuid());

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IMilestoneMembershipCoordinator> _membership = new();
    private readonly Mock<IOutboxWriter> _outbox = new();
    private readonly Mock<IInboxRealtimePublisher> _realtime = new();
    private readonly Mock<IFileStorageService> _files = new();
    private readonly Resolver _resolver = new();
    private readonly InMemoryConversationRepository _repo = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly List<WorkNotificationPayload> _notifications = new();
    private readonly RecordingActivityListener _activity = new();
    private Guid _conversationId;

    public ConversationsV2Tests()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _identity.Setup(x => x.ResolveIdentitiesByEmployeeIdAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<Guid> ids, CancellationToken _) => ids.Distinct().ToDictionary(id => id, id => new EmployeeIdentityDto(NameOf(id), null)));
        foreach (var id in new[] { Requester, Approver, Subject, Third, Fourth, Hr, Stranger })
            _membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Employee { Id = id, TenantId = TenantId, UserId = Guid.NewGuid() });
        _outbox.Setup(x => x.EnqueueAsync(It.IsAny<string>(), It.IsAny<WorkNotificationPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((string _, WorkNotificationPayload p, Guid? _, CancellationToken _) => _notifications.Add(p))
            .Returns(Task.CompletedTask);
        // An alert-like item: the subject employee and the reviewer are base; HR may view from their own scope.
        _resolver.Subject = new ConversationSubject(new[] { Subject, Approver }, "Unusual activity", null, SubjectEmployeeId: Subject);
        _resolver.Viewers.Add(Hr);
    }

    private static string NameOf(Guid id)
        => id == Requester ? "Bala" : id == Approver ? "Anu" : id == Subject ? "Sam" : id == Third ? "Priya" : id == Fourth ? "Fatima"
            : id == Hr ? "Hema" : "Someone";

    private void Caller(Guid employeeId)
        => _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(employeeId);

    private ConversationAccess Access() => new(new IConversationSubjectResolver[] { _resolver }, _repo);
    private ConversationResponseBuilder Builder() => new(_identity.Object, _repo);
    private ConversationNotifier Notifier() => new(_membership.Object, _outbox.Object);

    private async Task<Result<ConversationResponse>> OpenAs(Guid employeeId)
    {
        Caller(employeeId);
        var result = await new OpenConversationCommandHandler(_currentUser.Object, _identity.Object, Access(), _repo, Builder())
            .Handle(new OpenConversationCommand(Item.Source, Item.Kind, Item.Id), default);
        if (result.IsSuccess)
            _conversationId = result.Value!.Id;
        return result;
    }

    private async Task AddAs(Guid caller, params Guid[] people)
    {
        Caller(caller);
        var result = await new AddConversationParticipantsCommandHandler(
                _currentUser.Object, _identity.Object, Access(), _repo, _uow, _membership.Object, Notifier(), Builder(), _realtime.Object,
                NullLogger<AddConversationParticipantsCommandHandler>.Instance, [_activity])
            .Handle(new AddConversationParticipantsCommand(_conversationId, people), default);
        result.IsSuccess.Should().BeTrue(result.Error);
    }

    private Task<Result<ConversationMessageResponse>> PostAs(
        Guid caller, string body = "<p>hello</p>", IReadOnlyList<Guid>? mentions = null, IReadOnlyList<Guid>? files = null, string? tag = null)
    {
        Caller(caller);
        return new PostConversationMessageCommandHandler(
                _currentUser.Object, _identity.Object, Access(), _repo, _uow, Notifier(), Builder(), _realtime.Object,
                NullLogger<PostConversationMessageCommandHandler>.Instance, _files.Object, [_activity])
            .Handle(new PostConversationMessageCommand(_conversationId, body, mentions, files, tag), default);
    }

    private Task<Result> RemoveAs(Guid caller, Guid employeeId)
    {
        Caller(caller);
        return new RemoveConversationParticipantCommandHandler(_currentUser.Object, _identity.Object, Access(), _repo, _uow, _realtime.Object,
                NullLogger<RemoveConversationParticipantCommandHandler>.Instance, [_activity])
            .Handle(new RemoveConversationParticipantCommand(_conversationId, employeeId), default);
    }

    private Task<Result> LeaveAs(Guid caller)
    {
        Caller(caller);
        return new LeaveConversationCommandHandler(_currentUser.Object, _identity.Object, Access(), _repo, _uow, _realtime.Object,
                NullLogger<LeaveConversationCommandHandler>.Instance)
            .Handle(new LeaveConversationCommand(_conversationId), default);
    }

    private Task<Result<ConversationMessageResponse>> EditAs(Guid caller, Guid messageId, string body, IReadOnlyList<Guid>? mentions = null)
    {
        Caller(caller);
        return new EditConversationMessageCommandHandler(_currentUser.Object, _identity.Object, Access(), _repo, _uow, Notifier(),
                _realtime.Object, NullLogger<EditConversationMessageCommandHandler>.Instance)
            .Handle(new EditConversationMessageCommand(_conversationId, messageId, body, mentions), default);
    }

    private Task<Result> DeleteAs(Guid caller, Guid messageId, bool forEveryone = true)
    {
        Caller(caller);
        return new DeleteConversationMessageCommandHandler(_currentUser.Object, _identity.Object, Access(), _repo, _uow, _realtime.Object,
                NullLogger<DeleteConversationMessageCommandHandler>.Instance)
            .Handle(new DeleteConversationMessageCommand(_conversationId, messageId, forEveryone), default);
    }

    private Task<Result<ConversationMessageResponse>> ReactAs(Guid caller, Guid messageId, string? emoji)
    {
        Caller(caller);
        return new ReactToConversationMessageCommandHandler(_currentUser.Object, _identity.Object, Access(), _repo, _uow, Builder(),
                _realtime.Object, NullLogger<ReactToConversationMessageCommandHandler>.Instance, [_activity])
            .Handle(new ReactToConversationMessageCommand(_conversationId, messageId, emoji), default);
    }

    /// <summary>Moves a message's send time into the past (the edit/delete window is measured from it).</summary>
    private void SentMinutesAgo(Guid messageId, int minutes)
        => _repo.Messages.Single(m => m.Id == messageId).CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-minutes);

    private Task<Result<ConversationMessagesPageResponse>> ListAs(Guid caller)
    {
        Caller(caller);
        return new ListConversationMessagesQueryHandler(_currentUser.Object, _identity.Object, Access(), _repo, Builder())
            .Handle(new ListConversationMessagesQuery(_conversationId, null, 50), default);
    }

    private Task<Result<ConversationAttachmentDownload>> DownloadAs(Guid caller, Guid attachmentId)
    {
        Caller(caller);
        return new GetConversationAttachmentQueryHandler(_currentUser.Object, _identity.Object, Access(), _repo, _files.Object)
            .Handle(new GetConversationAttachmentQuery(_conversationId, attachmentId), default);
    }

    private Guid UploadedFile(long size = 1024, Guid? uploadedBy = null)
    {
        var id = Guid.NewGuid();
        _files.Setup(f => f.GetRecordAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(new FileRecordDto(id, TenantId, "k", "evidence.pdf", "evidence.pdf",
                "application/pdf", size, "sha", "clean", DateTimeOffset.UtcNow, uploadedBy ?? UserId, null)));
        _files.Setup(f => f.OpenReadAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result<FileStreamDto>.Success(new FileStreamDto(new MemoryStream([1, 2, 3]), "application/pdf")));
        return id;
    }

    private async Task OpenedWithThirdAddedByApprover()
    {
        (await OpenAs(Approver)).IsSuccess.Should().BeTrue();
        await AddAs(Approver, Third);
    }

    // ---- remove / leave ---------------------------------------------------------------------

    [Fact]
    public async Task A_base_participant_removes_an_added_member_who_loses_access_at_once_while_their_messages_stay()
    {
        await OpenedWithThirdAddedByApprover();
        var posted = await PostAs(Third, "<p>I saw it too</p>");

        _realtime.Invocations.Clear();
        var result = await RemoveAs(Subject, Third);

        result.IsSuccess.Should().BeTrue(result.Error);
        _repo.Participants.Single(p => p.EmployeeId == Third).RemovedByEmployeeId.Should().Be(Subject);
        (await ListAs(Third)).StatusCode.Should().Be(404);
        var history = (await ListAs(Approver)).Value!.Items;
        history.Should().Contain(m => m.Id == posted.Value!.Id);
        history.Last().BodyHtml.Should().Be("Sam removed Priya");
        _realtime.Verify(r => r.ParticipantRemovedAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(Third)),
            _conversationId, Third, It.IsAny<CancellationToken>()), Times.Once);
        _realtime.Verify(r => r.InboxChangedAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == Third), Item,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Whoever_added_a_member_may_remove_them_but_another_added_member_may_not()
    {
        await OpenedWithThirdAddedByApprover();
        await AddAs(Third, Fourth);

        (await RemoveAs(Fourth, Third)).StatusCode.Should().Be(403);
        (await RemoveAs(Third, Fourth)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Base_participants_and_viewers_cannot_be_removed()
    {
        await OpenedWithThirdAddedByApprover();
        (await OpenAs(Hr)).IsSuccess.Should().BeTrue();

        (await RemoveAs(Approver, Subject)).StatusCode.Should().Be(422);
        (await RemoveAs(Approver, Hr)).StatusCode.Should().Be(422);
        (await RemoveAs(Approver, Stranger)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task An_added_member_can_leave_but_a_base_participant_cannot()
    {
        await OpenedWithThirdAddedByApprover();

        (await LeaveAs(Approver)).StatusCode.Should().Be(422);
        (await LeaveAs(Third)).IsSuccess.Should().BeTrue();
        _repo.Participants.Single(p => p.EmployeeId == Third).LeftAt.Should().NotBeNull();
        (await ListAs(Third)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task A_removed_member_who_is_added_again_gets_access_back()
    {
        await OpenedWithThirdAddedByApprover();
        await RemoveAs(Approver, Third);

        await AddAs(Approver, Third);

        (await ListAs(Third)).IsSuccess.Should().BeTrue();
        _repo.Participants.Where(p => p.EmployeeId == Third).Should().ContainSingle().Which.IsActive.Should().BeTrue();
    }

    // ---- viewers ----------------------------------------------------------------------------

    [Fact]
    public async Task Someone_who_can_view_the_item_from_their_own_scope_joins_as_a_viewer_and_a_stranger_does_not()
    {
        (await OpenAs(Approver)).IsSuccess.Should().BeTrue();

        var hr = await OpenAs(Hr);
        var stranger = await OpenAs(Stranger);

        hr.IsSuccess.Should().BeTrue();
        hr.Value!.Participants.Single(p => p.EmployeeId == Hr).JoinReason.Should().Be(ConversationJoinReasons.Viewer);
        _repo.Participants.Should().ContainSingle(p => p.EmployeeId == Hr && p.JoinReason == ConversationJoinReasons.Viewer);
        stranger.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task A_viewer_whose_scope_is_revoked_loses_access_even_though_their_viewer_row_remains()
    {
        (await OpenAs(Approver)).IsSuccess.Should().BeTrue();
        (await OpenAs(Hr)).IsSuccess.Should().BeTrue();

        _resolver.Viewers.Remove(Hr);

        (await ListAs(Hr)).StatusCode.Should().Be(404);
        (await PostAs(Hr)).StatusCode.Should().Be(404);
        _repo.Participants.Should().ContainSingle(p => p.EmployeeId == Hr && p.JoinReason == ConversationJoinReasons.Viewer);
    }

    // ---- edit / delete ----------------------------------------------------------------------

    [Fact]
    public async Task The_author_edits_the_previous_text_is_kept_and_only_newly_mentioned_people_are_notified()
    {
        await OpenedWithThirdAddedByApprover();
        var posted = (await PostAs(Subject, "<p>first</p>", mentions: [Approver])).Value!;
        _notifications.Clear();

        var edited = await EditAs(Subject, posted.Id, "<p>second</p>", mentions: [Approver, Third]);

        edited.IsSuccess.Should().BeTrue(edited.Error);
        edited.Value!.BodyHtml.Should().Be("<p>second</p>");
        edited.Value.EditedAt.Should().NotBeNull();
        _repo.Revisions.Should().ContainSingle(r => r.MessageId == posted.Id && r.Kind == ConversationRevisionKinds.Edit
            && r.BodyHtml == "<p>first</p>" && r.RevisedByEmployeeId == Subject);
        _notifications.Should().ContainSingle(n => n.TemplateCode == ConversationNotifier.MentionedTemplate);
        _realtime.Verify(r => r.MessageUpdatedAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), _conversationId,
            It.Is<ConversationMessageResponse>(m => m.Id == posted.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Only_the_author_can_edit_or_delete()
    {
        await OpenedWithThirdAddedByApprover();
        var posted = (await PostAs(Subject)).Value!;

        (await EditAs(Approver, posted.Id, "<p>mine now</p>")).StatusCode.Should().Be(403);
        (await DeleteAs(Approver, posted.Id)).StatusCode.Should().Be(403);
        _repo.Revisions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_deleted_message_shows_as_a_placeholder_and_its_full_text_is_kept_in_the_audit()
    {
        await OpenedWithThirdAddedByApprover();
        var file = UploadedFile();
        var posted = (await PostAs(Subject, "<p>secret</p>", files: [file])).Value!;

        (await DeleteAs(Subject, posted.Id)).IsSuccess.Should().BeTrue();

        var shown = (await ListAs(Approver)).Value!.Items.Single(m => m.Id == posted.Id);
        shown.IsDeleted.Should().BeTrue();
        shown.BodyHtml.Should().BeEmpty();
        shown.Attachments.Should().BeEmpty();
        _repo.Revisions.Should().ContainSingle(r => r.Kind == ConversationRevisionKinds.Delete && r.BodyHtml == "<p>secret</p>");
        (await DownloadAs(Approver, posted.Attachments!.Single().Id)).StatusCode.Should().Be(404);
        (await EditAs(Subject, posted.Id, "<p>again</p>")).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Once_another_participant_has_read_a_message_it_cannot_be_deleted_for_everyone_but_can_be_hidden_for_self()
    {
        await OpenedWithThirdAddedByApprover();
        var posted = (await PostAs(Subject, "<p>keep for everyone</p>")).Value!;
        _repo.Participants.Single(p => p.EmployeeId == Approver).LastReadAt = posted.CreatedAt.AddSeconds(1);

        var globalDelete = await DeleteAs(Subject, posted.Id);
        var selfDelete = await DeleteAs(Subject, posted.Id, forEveryone: false);

        globalDelete.StatusCode.Should().Be(409);
        globalDelete.Error.Should().Be(DeleteConversationMessageCommandHandler.AlreadyRead);
        selfDelete.IsSuccess.Should().BeTrue(selfDelete.Error);
        (await ListAs(Subject)).Value!.Items.Should().NotContain(m => m.Id == posted.Id);
        (await ListAs(Approver)).Value!.Items.Should().Contain(m => m.Id == posted.Id && !m.IsDeleted);
        var subjectChat = (await _repo.ListMemberConversationsAsync(TenantId, Subject)).Single(c => c.Conversation.Id == _conversationId);
        subjectChat.LatestMessage?.Id.Should().NotBe(posted.Id, "a message hidden for me must not remain in my chat-list preview");
    }

    [Fact]
    public async Task A_message_can_still_be_edited_within_30_minutes_after_another_participant_reads_it()
    {
        await OpenedWithThirdAddedByApprover();
        var posted = (await PostAs(Subject, "<p>typo</p>")).Value!;
        _repo.Participants.Single(p => p.EmployeeId == Approver).LastReadAt = posted.CreatedAt.AddSeconds(1);

        var edited = await EditAs(Subject, posted.Id, "<p>corrected</p>");

        edited.IsSuccess.Should().BeTrue(edited.Error);
        edited.Value!.BodyHtml.Should().Be("<p>corrected</p>");
        edited.Value.EditedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_message_cannot_be_edited_after_30_minutes_even_when_it_is_unread()
    {
        await OpenedWithThirdAddedByApprover();
        var posted = (await PostAs(Subject, "<p>old typo</p>")).Value!;
        _repo.Messages.Single(m => m.Id == posted.Id).CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-31);

        var edited = await EditAs(Subject, posted.Id, "<p>too late</p>");

        edited.StatusCode.Should().Be(422);
        edited.Error.Should().Be("A message can only be changed within 30 minutes of sending it.");
    }

    // ---- attachments ------------------------------------------------------------------------

    [Fact]
    public async Task Attachments_download_for_participants_only_and_never_for_a_removed_member()
    {
        await OpenedWithThirdAddedByApprover();
        var posted = (await PostAs(Third, "", files: [UploadedFile()])).Value!;
        var attachment = posted.Attachments!.Single();
        attachment.FileName.Should().Be("evidence.pdf");

        (await DownloadAs(Approver, attachment.Id)).IsSuccess.Should().BeTrue();
        await RemoveAs(Approver, Third);
        (await DownloadAs(Third, attachment.Id)).StatusCode.Should().Be(404);
        (await DownloadAs(Stranger, attachment.Id)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Attachment_limits_are_enforced()
    {
        await OpenedWithThirdAddedByApprover();

        (await PostAs(Subject, files: Enumerable.Range(0, 6).Select(_ => UploadedFile()).ToList())).StatusCode.Should().Be(422);
        (await PostAs(Subject, files: [UploadedFile(size: 26L * 1024 * 1024)])).StatusCode.Should().Be(422);
        (await PostAs(Subject, files: [UploadedFile(uploadedBy: Guid.NewGuid())])).StatusCode.Should().Be(422);
        (await PostAs(Subject, files: [Guid.NewGuid()])).StatusCode.Should().Be(422);
        _repo.Messages.Where(m => m.Kind == ConversationMessageKinds.Text).Should().BeEmpty();
    }

    // ---- tags -------------------------------------------------------------------------------

    [Fact]
    public async Task Only_the_employee_the_item_is_about_can_post_a_justification()
    {
        await OpenedWithThirdAddedByApprover();

        (await PostAs(Approver, tag: ConversationMessageTags.Justification)).StatusCode.Should().Be(403);
        (await PostAs(Subject, tag: "info_request")).StatusCode.Should().Be(422);
        var justified = await PostAs(Subject, "<p>I was in a client meeting</p>", tag: ConversationMessageTags.Justification);

        justified.IsSuccess.Should().BeTrue();
        justified.Value!.Tag.Should().Be(ConversationMessageTags.Justification);
        (await OpenAs(Approver)).Value!.SubjectEmployeeId.Should().Be(Subject);
    }

    // ---- 30-minute edit/delete window -------------------------------------------------------------------------

    [Fact]
    public async Task The_author_can_edit_and_delete_within_30_minutes_of_sending()
    {
        (await OpenAs(Approver)).IsSuccess.Should().BeTrue();
        var message = (await PostAs(Approver, "<p>first</p>")).Value!;
        SentMinutesAgo(message.Id, 29);

        (await EditAs(Approver, message.Id, "<p>fixed</p>")).IsSuccess.Should().BeTrue();
        (await DeleteAs(Approver, message.Id)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task After_30_minutes_a_message_can_no_longer_be_edited_or_deleted()
    {
        (await OpenAs(Approver)).IsSuccess.Should().BeTrue();
        var message = (await PostAs(Approver, "<p>first</p>")).Value!;
        SentMinutesAgo(message.Id, 31);

        var edit = await EditAs(Approver, message.Id, "<p>too late</p>");
        var delete = await DeleteAs(Approver, message.Id);

        edit.StatusCode.Should().Be(422);
        edit.Error.Should().Be("A message can only be changed within 30 minutes of sending it.");
        delete.StatusCode.Should().Be(422);
        var stored = _repo.Messages.Single(m => m.Id == message.Id);
        (stored.BodyHtml, stored.IsRetracted).Should().Be(("<p>first</p>", false));
    }

    // ---- Reactions: one per person, replaced on re-react -----------------------------------------------------

    [Fact]
    public async Task Everyone_in_the_chat_reacts_once_and_reacting_again_replaces_the_earlier_emoji()
    {
        await OpenedWithThirdAddedByApprover();
        var message = (await PostAs(Approver)).Value!;

        (await ReactAs(Approver, message.Id, "👍")).IsSuccess.Should().BeTrue();
        (await ReactAs(Third, message.Id, "👍")).IsSuccess.Should().BeTrue();
        var replaced = await ReactAs(Approver, message.Id, "🎉");

        replaced.IsSuccess.Should().BeTrue(replaced.Error);
        _repo.Reactions.Where(r => r.MessageId == message.Id).Select(r => (r.EmployeeId, r.Emoji))
            .Should().BeEquivalentTo(new[] { (Third, "👍"), (Approver, "🎉") });
        replaced.Value!.Reactions!.Select(r => (r.Emoji, string.Join(",", r.EmployeeIds)))
            .Should().Equal(("👍", Third.ToString()), ("🎉", Approver.ToString()));
        _realtime.Verify(r => r.MessageUpdatedAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), _conversationId,
            It.Is<ConversationMessageResponse>(m => m.Id == message.Id), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Removing_a_reaction_leaves_the_others()
    {
        await OpenedWithThirdAddedByApprover();
        var message = (await PostAs(Approver)).Value!;
        await ReactAs(Approver, message.Id, "👍");
        await ReactAs(Third, message.Id, "😂");

        var removed = await ReactAs(Approver, message.Id, null);

        removed.Value!.Reactions!.Should().ContainSingle().Which.Emoji.Should().Be("😂");
        (await ListAs(Approver)).Value!.Items.Single(m => m.Id == message.Id).Reactions!.Should().ContainSingle();
    }

    [Fact]
    public async Task Deleted_and_system_messages_take_no_reactions_and_an_emoji_must_be_one_short_symbol()
    {
        await OpenedWithThirdAddedByApprover();
        var message = (await PostAs(Approver)).Value!;
        var system = _repo.Messages.First(m => m.Kind == ConversationMessageKinds.System);

        (await ReactAs(Approver, system.Id, "👍")).StatusCode.Should().Be(422);
        (await ReactAs(Approver, message.Id, "<b>")).StatusCode.Should().Be(422);
        (await ReactAs(Approver, message.Id, "two words")).StatusCode.Should().Be(422);
        await DeleteAs(Approver, message.Id);
        (await ReactAs(Third, message.Id, "👍")).StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Someone_outside_the_chat_cannot_react()
    {
        (await OpenAs(Approver)).IsSuccess.Should().BeTrue();
        var message = (await PostAs(Approver)).Value!;

        (await ReactAs(Stranger, message.Id, "👍")).StatusCode.Should().Be(404);
        _repo.Reactions.Should().BeEmpty();
    }

    // ---- Chat activity is reported (an exception case's reviewer acknowledges it) ----------------------------

    [Fact]
    public async Task Posting_adding_removing_and_reacting_are_reported_with_the_item_and_the_actor()
    {
        await OpenedWithThirdAddedByApprover();
        var message = (await PostAs(Approver)).Value!;
        await ReactAs(Approver, message.Id, "👍");
        (await RemoveAs(Approver, Third)).IsSuccess.Should().BeTrue();

        _activity.Calls.Should().Equal(
            (Item, Approver, ConversationActivities.MemberAdded),
            (Item, Approver, ConversationActivities.MessagePosted),
            (Item, Approver, ConversationActivities.Reacted),
            (Item, Approver, ConversationActivities.MemberRemoved));
    }

    [Fact]
    public async Task A_failing_activity_listener_never_fails_the_chat_request()
    {
        (await OpenAs(Approver)).IsSuccess.Should().BeTrue();
        _activity.Throw = true;

        var posted = await PostAs(Approver);

        posted.IsSuccess.Should().BeTrue(posted.Error);
        _repo.Messages.Should().Contain(m => m.Id == posted.Value!.Id);
    }

    private sealed class RecordingActivityListener : IConversationActivityListener
    {
        public List<(ItemRef Subject, Guid Actor, string Activity)> Calls { get; } = new();
        public bool Throw { get; set; }

        public Task OnActivityAsync(Guid tenantId, ItemRef subject, Guid actorEmployeeId, string activity, CancellationToken ct)
        {
            if (Throw)
                throw new InvalidOperationException("listener down");
            Calls.Add((subject, actorEmployeeId, activity));
            return Task.CompletedTask;
        }
    }

    // ---- Replies ---------------------------------------------------------------------------------

    private Task<Result<ConversationMessageResponse>> ReplyAs(Guid caller, Guid replyTo, string body = "<p>reply</p>")
    {
        Caller(caller);
        return new PostConversationMessageCommandHandler(
                _currentUser.Object, _identity.Object, Access(), _repo, _uow, Notifier(), Builder(), _realtime.Object,
                NullLogger<PostConversationMessageCommandHandler>.Instance, _files.Object)
            .Handle(new PostConversationMessageCommand(_conversationId, body, null, null, null, replyTo), default);
    }

    [Fact]
    public async Task A_reply_carries_a_short_plain_quote_of_the_message_it_answers()
    {
        await OpenAs(Subject);
        var original = (await PostAs(Subject, "<p>I was <b>on a call</b> at 9.</p>")).Value!;

        var reply = await ReplyAs(Approver, original.Id);

        reply.IsSuccess.Should().BeTrue(reply.Error);
        reply.Value!.ReplyTo.Should().NotBeNull();
        reply.Value.ReplyTo!.Id.Should().Be(original.Id);
        reply.Value.ReplyTo.AuthorName.Should().Be("Sam");
        reply.Value.ReplyTo.Excerpt.Should().Be("I was on a call at 9.");
        _repo.Messages.Single(m => m.Id == reply.Value.Id).ReplyToMessageId.Should().Be(original.Id);
    }

    [Fact]
    public async Task A_replied_to_message_is_seen_so_it_can_only_be_deleted_for_the_author()
    {
        await OpenAs(Subject);
        var original = (await PostAs(Subject, "<p>first</p>")).Value!;
        await ReplyAs(Approver, original.Id);
        var globalDelete = await DeleteAs(Subject, original.Id);
        (await DeleteAs(Subject, original.Id, forEveryone: false)).IsSuccess.Should().BeTrue();

        var opened = (await OpenAs(Approver)).Value!;

        globalDelete.StatusCode.Should().Be(409);
        var reply = opened.Messages.Single(m => m.ReplyTo is not null);
        reply.ReplyTo!.IsDeleted.Should().BeFalse();
        reply.ReplyTo.Excerpt.Should().Be("first");
    }

    [Fact]
    public async Task A_reply_to_a_message_of_another_conversation_or_a_system_message_is_rejected()
    {
        await OpenAs(Subject);
        var elsewhere = new ConversationMessage
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ConversationId = Guid.NewGuid(), AuthorEmployeeId = Subject,
            Kind = ConversationMessageKinds.Text, BodyHtml = "<p>other chat</p>", CreatedAt = DateTimeOffset.UtcNow
        };
        var system = new ConversationMessage
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ConversationId = _conversationId,
            Kind = ConversationMessageKinds.System, BodyHtml = "Hema was added", CreatedAt = DateTimeOffset.UtcNow
        };
        _repo.Messages.AddRange([elsewhere, system]);

        (await ReplyAs(Subject, elsewhere.Id)).Error.Should().Be("The message you are replying to is not in this conversation.");
        (await ReplyAs(Subject, system.Id)).Error.Should().Be("You cannot reply to that message.");
    }

    [Fact]
    public async Task An_item_with_evidence_gets_one_empty_evidence_message_at_its_time_that_is_never_unread()
    {
        var detectedAt = DateTimeOffset.UtcNow.AddHours(-3);
        _resolver.Subject = _resolver.Subject! with { EvidenceAt = detectedAt };

        await OpenAs(Subject);
        await OpenAs(Approver);
        await PostAs(Subject, "<p>I can explain</p>");

        var evidence = _repo.Messages.Where(m => m.Kind == ConversationMessageKinds.Evidence).ToList();
        evidence.Should().ContainSingle();
        evidence[0].BodyHtml.Should().BeEmpty();
        evidence[0].AuthorEmployeeId.Should().BeNull();
        evidence[0].CreatedAt.Should().Be(detectedAt);
        evidence[0].Id.Should().Be(OpenConversationCommandHandler.EvidenceMessageId(_conversationId));

        var unread = await _repo.CountUnreadAsync(TenantId, Approver, [Item]);
        unread[Item].Should().Be(1);

        var reply = await ReplyAs(Approver, evidence[0].Id, "<p>The photo looks fine to me</p>");
        reply.Value!.ReplyTo!.Kind.Should().Be(ConversationMessageKinds.Evidence);
        reply.Value.ReplyTo.Excerpt.Should().BeEmpty();
    }

    private sealed class Resolver : IConversationSubjectResolver
    {
        public ConversationSubject? Subject { get; set; }
        public HashSet<Guid> Viewers { get; } = new();
        public string Source => Item.Source;

        public Task<ConversationSubject?> ResolveAsync(Guid tenantId, ItemRef subject, CancellationToken ct)
            => Task.FromResult(subject == Item ? Subject : null);

        public Task<bool> CanViewAsync(Guid tenantId, ItemRef subject, Guid employeeId, CancellationToken ct)
            => Task.FromResult(subject == Item && Viewers.Contains(employeeId));
    }
}
