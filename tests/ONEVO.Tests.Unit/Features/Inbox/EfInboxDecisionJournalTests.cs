using FluentAssertions;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Inbox;
using ONEVO.Tests.Unit.Features.Auth;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Inbox;

public sealed class EfInboxDecisionJournalTests : IDisposable
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly TestClock _clock = new();

    public EfInboxDecisionJournalTests()
    {
        _connectionString = $"Data Source=inbox_journal_{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=False";
        _masterConnection = new SqliteConnection(_connectionString);
        _masterConnection.Open();
        using var schemaContext = CreateContext();
        schemaContext.Database.EnsureCreated();
    }

    public void Dispose() => _masterConnection.Dispose();

    private static InboxDecisionJournalEntry Entry(ItemRef item, string action, int minutes, Guid? actor = null, Guid? tenant = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenant ?? TenantId, Source = item.Source, Kind = item.Kind, ItemId = item.Id, Action = action,
        ActorEmployeeId = actor ?? Me, DecidedAt = T0.AddMinutes(minutes),
    };

    [Fact]
    public async Task Latest_escalations_are_one_per_item_of_the_source_and_kind_skipping_undone_ones()
    {
        var a = new ItemRef("calendar", "meeting_exception", Guid.NewGuid());
        var b = new ItemRef("calendar", "meeting_exception", Guid.NewGuid());
        Guid undoneId;
        await using (var db = CreateContext())
        {
            var journal = new EfInboxDecisionJournal(db);
            await journal.RecordAsync(Entry(a, InboxJournalActions.Escalate, 5));
            await journal.RecordAsync(Entry(a, InboxJournalActions.Escalate, 10));
            await journal.RecordAsync(Entry(a, InboxJournalActions.Resolve("excused"), 20));
            await journal.RecordAsync(Entry(b, InboxJournalActions.Escalate, 5));
            var undone = Entry(b, InboxJournalActions.Escalate, 15);
            undone.RevertedByJournalId = Guid.NewGuid();
            undoneId = undone.Id;
            await journal.RecordAsync(undone);
            await journal.RecordAsync(Entry(new ItemRef("monitoring", "exception_case", Guid.NewGuid()), InboxJournalActions.Escalate, 5));
            await journal.RecordAsync(Entry(a, InboxJournalActions.Escalate, 30, tenant: Guid.NewGuid()));
        }

        await using var read = CreateContext();
        var latest = await new EfInboxDecisionJournal(read).ListLatestEscalationsAsync(TenantId, "calendar", "meeting_exception");

        latest.Select(e => (e.ItemId, e.DecidedAt)).Should().BeEquivalentTo(new[] { (a.Id, T0.AddMinutes(10)), (b.Id, T0.AddMinutes(5)) });
        latest.Should().NotContain(e => e.Id == undoneId);
    }

    [Fact]
    public async Task Item_entries_are_oldest_first_and_latest_is_the_newest()
    {
        var item = new ItemRef("leave", "request", Guid.NewGuid());
        await using (var db = CreateContext())
        {
            var journal = new EfInboxDecisionJournal(db);
            await journal.RecordAsync(Entry(item, InboxJournalActions.Approve, 20));
            await journal.RecordAsync(Entry(item, InboxJournalActions.Forward, 5));
            await journal.RecordAsync(Entry(new ItemRef("leave", "request", Guid.NewGuid()), InboxJournalActions.Reject, 30));
            await journal.RecordAsync(Entry(item, InboxJournalActions.Reject, 40, tenant: Guid.NewGuid()));
        }

        await using var read = CreateContext();
        var reader = new EfInboxDecisionJournal(read);
        (await reader.ListForItemAsync(TenantId, item)).Select(e => e.Action).Should().Equal("forward", "approve");
        (await reader.LatestAsync(TenantId, item))!.Action.Should().Be("approve");
    }

    [Fact]
    public async Task By_actor_pages_newest_first_within_the_range()
    {
        await using (var db = CreateContext())
        {
            var journal = new EfInboxDecisionJournal(db);
            for (var i = 0; i < 5; i++)
                await journal.RecordAsync(Entry(new ItemRef("leave", "request", Guid.NewGuid()), InboxJournalActions.Approve, i * 10));
            await journal.RecordAsync(Entry(new ItemRef("leave", "request", Guid.NewGuid()), InboxJournalActions.Approve, 15, actor: Guid.NewGuid()));
        }

        await using var read = CreateContext();
        var page = await new EfInboxDecisionJournal(read).ListByActorAsync(TenantId, Me, T0.AddMinutes(10), null, "leave", null, 1, 2);

        page.Total.Should().Be(4);
        page.Entries.Select(e => e.DecidedAt).Should().Equal(T0.AddMinutes(40), T0.AddMinutes(30));
    }

    [Fact]
    public async Task MarkReverted_records_the_revert_row_and_links_the_original()
    {
        var item = new ItemRef("attendance", "correction", Guid.NewGuid());
        var original = Entry(item, InboxJournalActions.Approve, 0);
        await using (var db = CreateContext())
            await new EfInboxDecisionJournal(db).RecordAsync(original);

        var revert = Entry(item, InboxJournalActions.Revert, 5);
        await using (var db = CreateContext())
        {
            var journal = new EfInboxDecisionJournal(db);
            var loaded = (await journal.LatestAsync(TenantId, item))!;
            await journal.MarkRevertedAsync(loaded, revert);
        }

        await using var read = CreateContext();
        var rows = await new EfInboxDecisionJournal(read).ListForItemAsync(TenantId, item);
        rows.Select(r => r.Action).Should().Equal("approve", "revert");
        rows[0].RevertedByJournalId.Should().Be(revert.Id);
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
