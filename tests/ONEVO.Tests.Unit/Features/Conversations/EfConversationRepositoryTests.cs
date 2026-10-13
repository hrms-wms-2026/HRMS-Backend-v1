using FluentAssertions;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Domain.Features.Conversations.Entities;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Conversations;
using ONEVO.Tests.Unit.Features.Auth;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Conversations;

public sealed class EfConversationRepositoryTests : IDisposable
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly TestClock _clock = new();

    public EfConversationRepositoryTests()
    {
        _connectionString = $"Data Source=conversations_{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=False";
        _masterConnection = new SqliteConnection(_connectionString);
        _masterConnection.Open();
        using var schemaContext = CreateContext();
        schemaContext.Database.EnsureCreated();
    }

    public void Dispose() => _masterConnection.Dispose();

    private static Conversation NewConversation(ItemRef subject, Guid? tenantId = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId ?? TenantId, SubjectSource = subject.Source, SubjectKind = subject.Kind, SubjectId = subject.Id
    };

    private static ConversationMessage NewMessage(Guid conversationId, Guid? author, string body = "<p>x</p>") => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ConversationId = conversationId, AuthorEmployeeId = author,
        Kind = author is null ? ConversationMessageKinds.System : ConversationMessageKinds.Text, BodyHtml = body
    };

    private static ItemRef NewRef(string kind = "approval") => new("work", kind, Guid.NewGuid());

    [Fact]
    public async Task GetOrCreate_returns_the_existing_conversation_when_the_subject_already_has_one()
    {
        var subject = NewRef();
        var winner = NewConversation(subject);
        await using (var db = CreateContext())
            await new EfConversationRepository(db).GetOrCreateAsync(winner);

        await using var loser = CreateContext();
        var result = await new EfConversationRepository(loser).GetOrCreateAsync(NewConversation(subject));

        result.Id.Should().Be(winner.Id);
        await using var read = CreateContext();
        (await read.Conversations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task The_same_subject_in_another_tenant_gets_its_own_conversation()
    {
        var subject = NewRef();
        await using (var db = CreateContext())
            await new EfConversationRepository(db).GetOrCreateAsync(NewConversation(subject));
        await using var other = CreateContext();
        var created = await new EfConversationRepository(other).GetOrCreateAsync(NewConversation(subject, Guid.NewGuid()));

        await using var read = CreateContext();
        (await read.Conversations.CountAsync()).Should().Be(2);
        (await new EfConversationRepository(read).GetBySubjectAsync(TenantId, subject))!.Id.Should().NotBe(created.Id);
    }

    [Fact]
    public async Task AddParticipantIfMissing_inserts_once()
    {
        var conversation = NewConversation(NewRef());
        await using (var db = CreateContext())
            await new EfConversationRepository(db).GetOrCreateAsync(conversation);

        ConversationParticipant Row() => new()
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ConversationId = conversation.Id, EmployeeId = Me,
            JoinReason = ConversationJoinReasons.Base, AddedAt = _clock.UtcNow
        };
        await using var a = CreateContext();
        (await new EfConversationRepository(a).AddParticipantIfMissingAsync(Row())).Should().BeTrue();
        await using var b = CreateContext();
        (await new EfConversationRepository(b).AddParticipantIfMissingAsync(Row())).Should().BeFalse();
        await using var read = CreateContext();
        (await new EfConversationRepository(read).ListParticipantsAsync(TenantId, conversation.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task ListMessages_pages_newest_first_before_a_cutoff_and_loads_mentions()
    {
        var conversation = NewConversation(NewRef());
        var messages = new List<ConversationMessage>();
        await using (var db = CreateContext())
        {
            var repo = new EfConversationRepository(db);
            await repo.GetOrCreateAsync(conversation);
            for (var i = 0; i < 3; i++)
            {
                _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
                var m = NewMessage(conversation.Id, Me, $"<p>{i}</p>");
                messages.Add(m);
                await repo.AddMessageAsync(m, i == 2
                    ? [new ConversationMessageMention { Id = Guid.NewGuid(), TenantId = TenantId, MessageId = m.Id, EmployeeId = Other }]
                    : []);
                await db.SaveChangesAsync();
            }
        }

        await using var read = CreateContext();
        var repoRead = new EfConversationRepository(read);
        var newest = await repoRead.ListMessagesAsync(TenantId, conversation.Id, before: null, take: 2);
        newest.Select(m => m.BodyHtml).Should().Equal("<p>2</p>", "<p>1</p>");
        var older = await repoRead.ListMessagesAsync(TenantId, conversation.Id, before: newest[1].CreatedAt, take: 10);
        older.Select(m => m.BodyHtml).Should().Equal("<p>0</p>");
        var mentions = await repoRead.ListMentionsAsync(TenantId, messages.Select(m => m.Id).ToList());
        mentions.Should().ContainSingle().Which.Value.Should().Equal(Other);
    }

    [Fact]
    public async Task CountUnread_counts_others_messages_after_my_last_read_per_subject()
    {
        var subject = NewRef();
        var quiet = NewRef();
        var conversation = NewConversation(subject);
        var quietConversation = NewConversation(quiet);
        await using (var db = CreateContext())
        {
            var repo = new EfConversationRepository(db);
            await repo.GetOrCreateAsync(conversation);
            await repo.GetOrCreateAsync(quietConversation);
            _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
            await repo.AddMessageAsync(NewMessage(conversation.Id, Other, "<p>before read</p>"), []);
            await db.SaveChangesAsync();
            await repo.AddParticipantIfMissingAsync(new ConversationParticipant
            {
                Id = Guid.NewGuid(), TenantId = TenantId, ConversationId = conversation.Id, EmployeeId = Me,
                JoinReason = ConversationJoinReasons.Base, AddedAt = _clock.UtcNow, LastReadAt = _clock.UtcNow.AddSeconds(30)
            });
            _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
            await repo.AddMessageAsync(NewMessage(conversation.Id, Other, "<p>new</p>"), []);
            await repo.AddMessageAsync(NewMessage(conversation.Id, null, "Anu added Priya"), []);
            await repo.AddMessageAsync(NewMessage(conversation.Id, Me, "<p>mine</p>"), []);
            conversation.LastMessageAt = _clock.UtcNow;
            repo.Update(conversation);
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var counts = await new EfConversationRepository(read).CountUnreadAsync(TenantId, Me, new[] { subject, quiet, NewRef() });

        counts.Should().ContainSingle().Which.Should().Be(new KeyValuePair<ItemRef, int>(subject, 2));
        (await new EfConversationRepository(read).CountUnreadAsync(TenantId, Other, new[] { subject }))[subject].Should().Be(2);
    }

    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new SqliteTestApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }

    private sealed class TestClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }

    private sealed class NoOpPublisher : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }
}
