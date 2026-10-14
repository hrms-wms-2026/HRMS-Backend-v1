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
                NullLogger<AddConversationParticipantsCommandHandler>.Instance)
            .Handle(new AddConversationParticipantsCommand(_conversationId, people), default);
        result.IsSuccess.Should().BeTrue(result.Error);
    }

    private Task<Result<ConversationMessageResponse>> PostAs(
        Guid caller, string body = "<p>hello</p>", IReadOnlyList<Guid>? mentions = null, IReadOnlyList<Guid>? files = null, string? tag = null)
    {
        Caller(caller);
        return new PostConversationMessageCommandHandler(
                _currentUser.Object, _identity.Object, Access(), _repo, _uow, Notifier(), Builder(), _realtime.Object,
                NullLogger<PostConversationMessageCommandHandler>.Instance, _files.Object)
            .Handle(new PostConversationMessageCommand(_conversationId, body, mentions, files, tag), default);
    }

    private Task<Result> RemoveAs(Guid caller, Guid employeeId)
    {
        Caller(caller);
        return new RemoveConversationParticipantCommandHandler(_currentUser.Object, _identity.Object, Access(), _repo, _uow, _realtime.Object,
                NullLogger<RemoveConversationParticipantCommandHandler>.Instance)
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

    private Task<Result> DeleteAs(Guid caller, Guid messageId)
    {
        Caller(caller);
        return new DeleteConversationMessageCommandHandler(_currentUser.Object, _identity.Object, Access(), _repo, _uow, _realtime.Object,
                NullLogger<DeleteConversationMessageCommandHandler>.Instance)
            .Handle(new DeleteConversationMessageCommand(_conversationId, messageId), default);
    }

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
        (await PostAs(Subject, tag: ConversationMessageTags.InfoRequest)).StatusCode.Should().Be(422);
        var justified = await PostAs(Subject, "<p>I was in a client meeting</p>", tag: ConversationMessageTags.Justification);

        justified.IsSuccess.Should().BeTrue();
        justified.Value!.Tag.Should().Be(ConversationMessageTags.Justification);
        (await OpenAs(Approver)).Value!.SubjectEmployeeId.Should().Be(Subject);
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
