using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.SharedPlatform;

public sealed class OutboxKeyedEnqueueIntegrationTests
{
    private sealed record Payload(string Value);

    private static readonly AesEncryptionService Encryption = new(
        Options.Create(new EncryptionOptions { MasterKey = "integration-test-master-key-32-chars-min" }));

    private static OutboxWriter Writer(ApplicationDbContext db) => new(db, Encryption, new SystemDateTimeProvider());

    private static Task<int> CountAsync(ApplicationDbContext db, string key) =>
        db.Set<OutboxMessage>().AsNoTracking().CountAsync(m => m.DeduplicationKey == key);

    [Fact]
    public async Task SameKeyTwiceInOneTransaction_InsertsOnce_SecondIsANoOp()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var key = $"test:{Guid.NewGuid()}";

        await using var tx = await db.Database.BeginTransactionAsync();
        var first = await Writer(db).EnqueueKeyedAsync("test_type", new Payload("a"), key, h.TenantId);
        var second = await Writer(db).EnqueueKeyedAsync("test_type", new Payload("b"), key, h.TenantId);
        await tx.CommitAsync();

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(1, await CountAsync(db, key));
        var row = await db.Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.DeduplicationKey == key);
        Assert.Equal(OutboxMessageStatus.Pending, row.Status);
        Assert.Contains("\"a\"", Encryption.Decrypt(row.EncryptedPayload));
    }

    [Fact]
    public async Task KeyedEnqueue_WithoutATransaction_Throws_AndInsertsNothing()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var key = $"test:{Guid.NewGuid()}";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Writer(db).EnqueueKeyedAsync("test_type", new Payload("a"), key, h.TenantId));

        Assert.Equal(0, await CountAsync(db, key));
    }

    [Fact]
    public async Task KeyAlreadyCommittedByAnEarlierRequest_IsANoOp_AndTheNewBusinessChangeStillCommits()
    {
        var h = await MyTeamDb.CreateAsync();
        var key = $"test:{Guid.NewGuid()}";
        await using (var earlier = h.NewContext())
        {
            await using var tx = await earlier.Database.BeginTransactionAsync();
            await Writer(earlier).EnqueueKeyedAsync("test_type", new Payload("first"), key, h.TenantId);
            await tx.CommitAsync();
        }

        await using var db = h.NewContext();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            // The "business change": an unkeyed, staged row that only SaveChanges persists.
            await Writer(db).EnqueueAsync("business_marker", new Payload("change"), h.TenantId);
            var inserted = await Writer(db).EnqueueKeyedAsync("test_type", new Payload("second"), key, h.TenantId);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            Assert.False(inserted);
        }

        await using var verify = h.NewContext();
        Assert.Equal(1, await CountAsync(verify, key));
        Assert.Equal(1, await verify.Set<OutboxMessage>().CountAsync(m => m.Type == "business_marker" && m.TenantId == h.TenantId));
    }

    [Fact]
    public async Task RolledBackTransaction_LeavesNoKeyedRow()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var key = $"test:{Guid.NewGuid()}";

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await Writer(db).EnqueueKeyedAsync("test_type", new Payload("a"), key, h.TenantId);
            await tx.RollbackAsync();
        }

        Assert.Equal(0, await CountAsync(db, key));
    }
}
