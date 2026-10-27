using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Inbox.Chats;
using ONEVO.Application.Features.Inbox.Services;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Conversations.Entities;
using ONEVO.Tests.Unit.Features.Conversations;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Inbox;

/// <summary>The chat info panel: mute, description, the chat's files and message search.</summary>
public class ChatInfoHandlersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Ravi = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryConversationRepository _repo = new();
    private readonly Mock<IConversationAccess> _access = new();
    private readonly Conversation _group;
    private readonly Conversation _direct;
    private readonly ChatInfoHandlers _handlers;

    public ChatInfoHandlersTests()
    {
        _group = AddChat(ConversationSources.Group);
        _direct = AddChat(ConversationSources.Direct);
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        currentUser.Setup(u => u.TenantId).Returns(TenantId);
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(i => i.ResolveCallerEmployeeIdAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Me);
        identity.Setup(i => i.ResolveIdentitiesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<Guid> ids, CancellationToken _) =>
                ids.ToDictionary(id => id, id => new EmployeeIdentityDto(id == Ravi ? "Ravi" : "Me", null)));
        _handlers = new ChatInfoHandlers(new InboxCallerResolver(currentUser.Object, identity.Object), _access.Object, _repo, identity.Object,
            Mock.Of<IPrimaryEntityAssetLinker>(), currentUser.Object, Mock.Of<IInboxRealtimePublisher>(), NullLogger<ChatInfoHandlers>.Instance);
    }

    private Conversation AddChat(string source)
    {
        var chat = new Conversation { Id = Guid.NewGuid(), TenantId = TenantId, SubjectSource = source, SubjectKind = source, SubjectId = Guid.NewGuid(), CreatedAt = Now };
        _repo.Conversations.Add(chat);
        foreach (var employee in new[] { Me, Ravi })
            _repo.Participants.Add(new ConversationParticipant { Id = Guid.NewGuid(), TenantId = TenantId, ConversationId = chat.Id, EmployeeId = employee, AddedAt = Now });
        _access.Setup(a => a.LoadAsync(TenantId, chat.Id, Me, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationContext(chat, new ItemRef(source, source, chat.SubjectId), new ConversationSubject([Me, Ravi], "Chat", null),
                [], [Me, Ravi], Me));
        return chat;
    }

    private ConversationMessage Post(Conversation chat, string html, Guid author, int minutes, params (string Name, string Type)[] files)
    {
        var message = new ConversationMessage
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ConversationId = chat.Id, AuthorEmployeeId = author, Kind = ConversationMessageKinds.Text,
            BodyHtml = html, CreatedAt = Now.AddMinutes(minutes),
        };
        _repo.Messages.Add(message);
        foreach (var (name, type) in files)
            _repo.Attachments.Add(new ConversationMessageAttachment
            {
                Id = Guid.NewGuid(), TenantId = TenantId, MessageId = message.Id, FileRecordId = Guid.NewGuid(), FileName = name, ContentType = type, SizeBytes = 10,
            });
        return message;
    }

    [Fact]
    public async Task Media_lists_every_kind_of_file_newest_first_with_counts_and_skips_retracted_and_hidden_messages()
    {
        Post(_group, "<p>plan</p>", Ravi, 1, ("plan.pdf", "application/pdf"));
        Post(_group, "<p>photos</p>", Ravi, 2, ("a.png", "image/png"), ("b.jpg", "image/jpeg"));
        Post(_group, "<p>clip</p>", Me, 3, ("clip.mp4", "video/mp4"));
        Post(_group, "<p>note</p>", Ravi, 4, ("voice.mp3", "audio/mpeg"));
        Post(_group, "<p>gone</p>", Ravi, 5, ("gone.png", "image/png")).DeletedAt = Now;
        var hidden = Post(_group, "<p>hidden</p>", Ravi, 6, ("hidden.png", "image/png"));
        _repo.MessageHides.Add(new ConversationMessageHide { Id = Guid.NewGuid(), TenantId = TenantId, MessageId = hidden.Id, EmployeeId = Me });

        var all = (await _handlers.Handle(new ListChatMediaQuery(_group.Id, null, 1, 3), CancellationToken.None)).Value!;
        var images = (await _handlers.Handle(new ListChatMediaQuery(_group.Id, "image", 1, 20), CancellationToken.None)).Value!;

        all.Total.Should().Be(5);
        all.Items.Select(i => i.FileName).Should().Equal("voice.mp3", "clip.mp4", "a.png");
        all.Items[0].Should().Match<ChatMediaItemResponse>(i => i.Kind == "audio" && i.AuthorName == "Ravi");
        all.Counts.Should().BeEquivalentTo(new Dictionary<string, int> { ["image"] = 2, ["video"] = 1, ["audio"] = 1, ["document"] = 1 });
        images.Items.Select(i => i.FileName).Should().BeEquivalentTo("a.png", "b.jpg");
        images.Total.Should().Be(2);
    }

    [Fact]
    public async Task Search_finds_text_but_not_markup_and_returns_a_snippet()
    {
        Post(_group, "<p>Payroll is <strong>ready</strong> for review</p>", Ravi, 1);
        Post(_group, "<p class=\"ready\">nothing here</p>", Ravi, 2);

        var hits = (await _handlers.Handle(new SearchChatMessagesQuery(_group.Id, "ready"), CancellationToken.None)).Value!;

        hits.Should().ContainSingle();
        hits[0].Snippet.Should().Be("Payroll is ready for review");
        hits[0].AuthorName.Should().Be("Ravi");
    }

    [Fact]
    public async Task A_description_belongs_to_groups_only_and_is_trimmed()
    {
        (await _handlers.Handle(new SetChatDescriptionCommand(_group.Id, "  Monthly payroll checks  "), CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await _handlers.Handle(new SetChatDescriptionCommand(_direct.Id, "Hi"), CancellationToken.None)).IsSuccess.Should().BeFalse();

        _group.Description.Should().Be("Monthly payroll checks");
        _direct.Description.Should().BeNull();
    }

    [Fact]
    public async Task A_muted_chat_leaves_the_inbox_badge_for_that_person_only()
    {
        Post(_direct, "<p>hi</p>", Ravi, 1);
        Post(_group, "<p>hello all</p>", Ravi, 2);
        var unread = new ConversationUnreadCounts(_repo);
        (await unread.TotalInChatsAsync(TenantId, Me, CancellationToken.None)).Should().Be(2);

        await _handlers.Handle(new SetChatMutedCommand(_group.Id, true), CancellationToken.None);

        (await unread.TotalInChatsAsync(TenantId, Me, CancellationToken.None)).Should().Be(1);
        (await _repo.ListMutedConversationIdsAsync(TenantId, Ravi)).Should().BeEmpty();
    }
}
