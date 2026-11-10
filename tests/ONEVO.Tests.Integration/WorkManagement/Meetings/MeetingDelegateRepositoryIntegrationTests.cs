using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Meeting delegates (spec "Model"): a grant is active while it is unrevoked, unexpired and both people are active;
/// one unrevoked grant per pair; the table is under tenant isolation.</summary>
public sealed class MeetingDelegateRepositoryIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private sealed record World(MyTeamDb H, Employee Principal, Employee Delegate);

    private static async Task<World> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var principal = h.AddEmployee(db);
        var delegateEmployee = h.AddEmployee(db);
        await db.SaveChangesAsync();
        return new World(h, principal, delegateEmployee);
    }

    private static MeetingDelegate Grant(World w, DateTimeOffset? expiresAt = null, DateTimeOffset? revokedAt = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = w.H.TenantId, PrincipalEmployeeId = w.Principal.Id, DelegateEmployeeId = w.Delegate.Id,
        CreatedByUserId = w.Principal.UserId, CreatedAt = Now.AddDays(-1), ExpiresAt = expiresAt, RevokedAt = revokedAt,
    };

    private static async Task AddAsync(MyTeamDb h, MeetingDelegate row)
    {
        await using var db = h.NewContext();
        new EfMeetingDelegateRepository(db).Add(row);
        await db.SaveChangesAsync();
    }

    private static async Task<bool> IsActiveAsync(World w, DateTimeOffset now)
    {
        await using var db = w.H.NewTenantContext();
        return await new EfMeetingDelegateRepository(db).IsActiveDelegateAsync(w.H.TenantId, w.Principal.Id, w.Delegate.Id, now);
    }

    [Fact]
    public async Task IsActiveDelegate_IsTrueForAnActiveGrant_AndFalseOnceRevoked()
    {
        var w = await SeedAsync();
        var row = Grant(w);
        await AddAsync(w.H, row);

        Assert.True(await IsActiveAsync(w, Now));

        await using (var db = w.H.NewContext())
        {
            var tracked = await new EfMeetingDelegateRepository(db).GetUnrevokedTrackedAsync(w.H.TenantId, row.Id);
            tracked!.RevokedAt = Now;
            await db.SaveChangesAsync();
        }

        Assert.False(await IsActiveAsync(w, Now));
    }

    [Fact]
    public async Task IsActiveDelegate_IsFalseOnceExpired()
    {
        var w = await SeedAsync();
        await AddAsync(w.H, Grant(w, expiresAt: Now.AddHours(1)));

        Assert.True(await IsActiveAsync(w, Now));
        Assert.False(await IsActiveAsync(w, Now.AddHours(1)));
    }

    [Fact]
    public async Task IsActiveDelegate_IsFalseWhenEitherEmployeeIsNotActive()
    {
        var w = await SeedAsync();
        await AddAsync(w.H, Grant(w));
        await using (var db = w.H.NewContext())
        {
            if (!await db.EmploymentStatuses.AnyAsync(s => s.Id == EmploymentStatusIds.Terminated))
                db.EmploymentStatuses.Add(new EmploymentStatus { Id = EmploymentStatusIds.Terminated, Code = "terminated", Label = "Terminated" });
            (await db.Employees.SingleAsync(e => e.Id == w.Delegate.Id)).EmploymentStatusId = EmploymentStatusIds.Terminated;
            await db.SaveChangesAsync();
        }

        Assert.False(await IsActiveAsync(w, Now));
        await using var read = w.H.NewTenantContext();
        Assert.Empty(await new EfMeetingDelegateRepository(read).ListActivePrincipalIdsAsync(w.H.TenantId, w.Delegate.Id, Now));
        Assert.Empty(await new EfMeetingDelegateRepository(read).ListActiveDelegateIdsAsync(w.H.TenantId, w.Principal.Id, Now));
    }

    [Fact]
    public async Task ASecondUnrevokedGrantForThePair_IsRejectedByTheUniqueIndex()
    {
        var w = await SeedAsync();
        await AddAsync(w.H, Grant(w));

        await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(w.H, Grant(w)));
        // A revoked row for the same pair does not block a new grant.
        await AddAsync(w.H, Grant(w, revokedAt: Now));
    }

    [Fact]
    public async Task AnotherTenant_CannotSeeTheRows()
    {
        var w = await SeedAsync();
        await AddAsync(w.H, Grant(w));
        var other = await w.H.AddTenantAsync();

        await using var otherDb = other.NewTenantContext();
        Assert.Empty(await otherDb.Set<MeetingDelegate>().IgnoreQueryFilters().ToListAsync());
        Assert.False(await new EfMeetingDelegateRepository(otherDb).IsActiveDelegateAsync(w.H.TenantId, w.Principal.Id, w.Delegate.Id, Now));
    }
}
