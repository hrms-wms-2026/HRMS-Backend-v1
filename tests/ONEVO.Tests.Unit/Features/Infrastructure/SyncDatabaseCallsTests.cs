using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Tests.Unit.Features.Auth;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Infrastructure;

/// <summary>
/// Improvements task 26: synchronous SaveChanges would block a thread-pool thread on the async
/// domain-event dispatch, so it is refused before anything is written. The async path still works.
/// </summary>
public sealed class SyncDatabaseCallsTests : IDisposable
{
    private readonly string _cs = $"Data Source=sync_calls_{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=False";
    private readonly SqliteConnection _pin;

    public SyncDatabaseCallsTests()
    {
        _pin = new SqliteConnection(_cs);
        _pin.Open();
        using var db = Create();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _pin.Dispose();

    private ApplicationDbContext Create()
    {
        var clock = new SystemDateTimeProvider();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_cs).UseSnakeCaseNamingConvention().Options;
        return new SqliteTestApplicationDbContext(options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), clock),
            new SoftDeleteInterceptor(clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }

    private static Session NewSession(string keyHash) => new()
    {
        Id = Guid.NewGuid(), UserId = Guid.NewGuid(), TenantId = Guid.NewGuid(),
        KeyHash = keyHash, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
    };

    [Fact]
    public async Task Sync_SaveChanges_is_refused_before_anything_is_written()
    {
        using (var db = Create())
        {
            db.Sessions.Add(NewSession("sync"));
            var save = () => db.SaveChanges();
            save.Should().Throw<InvalidOperationException>().WithMessage("*SaveChangesAsync*");
        }

        await using var check = Create();
        (await check.Sessions.CountAsync(s => s.KeyHash == "sync")).Should().Be(0);
    }

    [Fact]
    public async Task SaveChangesAsync_still_saves()
    {
        await using (var db = Create())
        {
            db.Sessions.Add(NewSession("async"));
            await db.SaveChangesAsync();
        }

        await using var check = Create();
        (await check.Sessions.CountAsync(s => s.KeyHash == "async")).Should().Be(1);
    }
}
