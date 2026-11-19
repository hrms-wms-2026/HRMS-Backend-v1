using FluentAssertions;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Tests.Unit.Features.Auth;
using Xunit;

namespace ONEVO.Tests.Unit.Infrastructure;

/// <summary>The Inbox wraps a module command (which may run its own ExecuteInTransactionAsync) and its journal row
/// in one transaction: a nested call joins the outer transaction instead of failing.</summary>
public sealed class UnitOfWorkNestingTests : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _master;
    private readonly Clock _clock = new();

    public UnitOfWorkNestingTests()
    {
        _connectionString = $"Data Source=uow_{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=False";
        _master = new SqliteConnection(_connectionString);
        _master.Open();
        using var db = Context();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _master.Dispose();

    private static InboxDecisionJournalEntry Row() => new()
    {
        Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Source = "leave", Kind = "request", ItemId = Guid.NewGuid(),
        Action = "approve", ActorEmployeeId = Guid.NewGuid(), DecidedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task A_nested_call_joins_the_outer_transaction_and_both_commit_together()
    {
        await using (var db = Context())
        {
            var uow = new UnitOfWork(db);
            await uow.ExecuteInTransactionAsync(async ct =>
            {
                db.InboxDecisionJournal.Add(Row());
                await uow.ExecuteInTransactionAsync(async inner =>
                {
                    db.InboxDecisionJournal.Add(Row());
                    await db.SaveChangesAsync(inner);
                    return true;
                }, ct);
                await db.SaveChangesAsync(ct);
                return true;
            });
        }

        await using var read = Context();
        (await read.InboxDecisionJournal.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task A_failure_after_the_nested_call_rolls_back_the_nested_work_too()
    {
        await using (var db = Context())
        {
            var uow = new UnitOfWork(db);
            var act = () => uow.ExecuteInTransactionAsync<bool>(async ct =>
            {
                await uow.ExecuteInTransactionAsync(async inner =>
                {
                    db.InboxDecisionJournal.Add(Row());
                    await db.SaveChangesAsync(inner);
                    return true;
                }, ct);
                throw new InvalidOperationException("journal write failed");
            });
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        await using var read = Context();
        (await read.InboxDecisionJournal.CountAsync()).Should().Be(0);
    }

    private ApplicationDbContext Context()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connectionString).UseSnakeCaseNamingConvention().Options;
        return new SqliteTestApplicationDbContext(options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock), new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()), new TenantContextAccessor());
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private sealed class NoOpPublisher : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }
}
