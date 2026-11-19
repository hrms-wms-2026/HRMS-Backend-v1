using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Queries.ExportInboxItem;
using ONEVO.Application.Features.Inbox.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Conversations.Entities;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Tests.Unit.Fakes;
using ONEVO.Tests.Unit.Features.Conversations;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Inbox;

public class ExportInboxItemQueryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Reviewer = Guid.NewGuid();
    private static readonly Guid Subject = Guid.NewGuid();
    private static readonly Guid Hr = Guid.NewGuid();
    private static readonly Guid Added = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly ItemRef Item = new("monitoring", "exception_case", Guid.NewGuid());

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IInboxRouter> _router = new();
    private readonly InMemoryConversationRepository _repo = new();
    private readonly InMemoryInboxDecisionJournal _journal = new();
    private readonly Resolver _resolver = new();
    private readonly Conversation _conversation = new() { Id = Guid.NewGuid(), TenantId = TenantId, SubjectSource = Item.Source, SubjectKind = Item.Kind, SubjectId = Item.Id };

    public ExportInboxItemQueryTests()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(UserId);
        _identity.Setup(i => i.ResolveIdentitiesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<Guid> ids, CancellationToken _) => ids.ToDictionary(id => id, id => new EmployeeIdentityDto(id == Subject ? "Sam" : "Anu", null)));
        var item = new InboxItem(Item.Source, Item.Kind, Item.Id, InboxCategories.Alert, "UnusualActivityPattern", "Unusual activity - Sam", null,
            "open", InboxDirections.Received, "medium", "legal_entity", null, null, null, null, T0, T0, [], 0, true,
            new Dictionary<string, IReadOnlyList<string>>());
        _router.Setup(r => r.GetDetailAsync(TenantId, It.IsAny<Guid>(), Item.Source, Item.Kind, Item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<InboxItemDetail>.Success(new InboxItemDetail(item,
                [new InboxStep("detected", "Detected", null, T0, InboxStepStates.Done, null)], "Score dropped", [], null, null, null, null)));
        _resolver.Subject = new ConversationSubject([Subject, Reviewer], "Unusual activity", null, SubjectEmployeeId: Subject);
        _resolver.Viewers.Add(Hr);

        _repo.Conversations.Add(_conversation);
        _repo.Participants.Add(new ConversationParticipant
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ConversationId = _conversation.Id, EmployeeId = Added,
            JoinReason = ConversationJoinReasons.Added, AddedAt = T0
        });
        var edited = Message(Subject, "<p>I was with a client</p>", T0.AddMinutes(1));
        edited.EditedAt = T0.AddMinutes(2);
        var deleted = Message(Reviewer, string.Empty, T0.AddMinutes(3));
        deleted.DeletedAt = T0.AddMinutes(4);
        _repo.Messages.AddRange([edited, deleted]);
        _repo.Revisions.AddRange([
            Revision(edited.Id, "<p>I was out</p>", ConversationRevisionKinds.Edit, T0.AddMinutes(2)),
            Revision(deleted.Id, "<p>looks fishy</p>", ConversationRevisionKinds.Delete, T0.AddMinutes(4))]);
        _repo.Attachments.Add(new ConversationMessageAttachment
        {
            Id = Guid.NewGuid(), TenantId = TenantId, MessageId = edited.Id, FileName = "calendar.png", ContentType = "image/png", SizeBytes = 2048
        });
        _journal.Add(TenantId, Item, InboxJournalActions.Acknowledge, Reviewer, T0.AddMinutes(5));
    }

    private ConversationMessage Message(Guid author, string body, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ConversationId = _conversation.Id, AuthorEmployeeId = author,
        Kind = ConversationMessageKinds.Text, BodyHtml = body, CreatedAt = at
    };

    private static ConversationMessageRevision Revision(Guid messageId, string body, string kind, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, MessageId = messageId, BodyHtml = body, Kind = kind, RevisedAt = at
    };

    private Task<Result<InboxExport>> ExportAs(Guid employeeId)
    {
        _identity.Setup(i => i.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(employeeId);
        return new ExportInboxItemQueryHandler(
                new InboxCallerResolver(_currentUser.Object, _identity.Object), _router.Object,
                new ConversationAccess([_resolver], _repo), _repo, _journal, _identity.Object, new FakeDateTimeProvider { UtcNow = T0.AddHours(1) })
            .Handle(new ExportInboxItemQuery(Item.Source, Item.Kind, Item.Id), default);
    }

    [Fact]
    public async Task The_decider_gets_the_full_bundle_with_every_edit_and_deleted_message_marked()
    {
        var result = await ExportAs(Reviewer);

        result.IsSuccess.Should().BeTrue(result.Error);
        var html = result.Value!.Html;
        html.Should().Contain("Unusual activity - Sam").And.Contain("Detected").And.Contain("acknowledge");
        html.Should().Contain("I was with a client").And.Contain(InboxInvestigationExport.EditedMarker).And.Contain("I was out");
        html.Should().Contain(InboxInvestigationExport.DeletedMarker).And.Contain("looks fishy");
        html.Should().Contain("calendar.png");
        result.Value.FileName.Should().EndWith(".html");
    }

    [Fact]
    public async Task Someone_who_reviews_from_their_own_scope_can_export()
        => (await ExportAs(Hr)).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData("added")]
    [InlineData("subject")]
    public async Task An_added_member_or_the_employee_it_is_about_cannot_export(string who)
    {
        var result = await ExportAs(who == "added" ? Added : Subject);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Seeing_the_item_is_not_enough_to_export_when_the_module_does_not_grant_review_scope()
    {
        _resolver.ViewersCanReview = false;

        (await ExportAs(Hr)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Chat_bodies_are_exported_as_text_never_as_markup()
    {
        _repo.Messages.Add(Message(Reviewer, "<p>see <a href=\"https://evil.example\" title=x>this</a><img src=x></p>", T0.AddMinutes(6)));
        _repo.Revisions.Add(Revision(_repo.Messages[0].Id, "<p><b>bold</b> claim</p>", ConversationRevisionKinds.Edit, T0.AddMinutes(7)));

        var html = (await ExportAs(Reviewer)).Value!.Html;

        html.Should().Contain("see this").And.Contain("bold claim");
        html.Should().NotContain("<a href").And.NotContain("<img").And.NotContain("<b>");
    }

    private sealed class Resolver : IConversationSubjectResolver
    {
        public ConversationSubject? Subject { get; set; }
        public HashSet<Guid> Viewers { get; } = new();
        public bool ViewersCanReview { get; set; } = true;
        public string Source => Item.Source;

        public Task<bool> CanReviewAsync(Guid tenantId, ItemRef subject, Guid employeeId, CancellationToken ct)
            => Task.FromResult(ViewersCanReview && subject == Item && Viewers.Contains(employeeId));

        public Task<ConversationSubject?> ResolveAsync(Guid tenantId, ItemRef subject, CancellationToken ct)
            => Task.FromResult(subject == Item ? Subject : null);

        public Task<bool> CanViewAsync(Guid tenantId, ItemRef subject, Guid employeeId, CancellationToken ct)
            => Task.FromResult(subject == Item && Viewers.Contains(employeeId));
    }
}
