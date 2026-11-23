using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.SqlServer;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.SharedPlatform;

/// <summary>
/// Task 42 on SQL Server: the rowversion token rejects a stale save on the tables users edit through forms, a client
/// version that no longer matches is rejected, and code that updates a row it loaded without tracking still works.
/// Covers the tables MyTeamDb can seed; RowVersionTokenModelTests checks the mapping of all of them. Requires Docker.
/// </summary>
public sealed class RowVersionConcurrencyIntegrationTests
{
    public static TheoryData<string> Tables => new() { "legal_entities", "departments", "positions", "clock_in_policies", "objectives", "tasks" };

    private static async Task<(MyTeamDb Db, Func<ApplicationDbContext, Task<object>> Load)> SeedAsync(string table)
    {
        var helper = await MyTeamDb.CreateAsync();
        await using var db = helper.NewContext();
        var lead = helper.AddEmployee(db);
        Guid id;
        Func<ApplicationDbContext, Task<object>> load;
        switch (table)
        {
            case "legal_entities":
                id = helper.LegalEntityId;
                load = async c => await c.LegalEntities.SingleAsync(e => e.Id == id);
                break;
            case "departments":
                id = helper.AddDepartment(db);
                load = async c => await c.Departments.SingleAsync(e => e.Id == id);
                break;
            case "positions":
                id = helper.AddPositionHeldBy(db, lead.Id);
                load = async c => await c.Positions.SingleAsync(e => e.Id == id);
                break;
            case "clock_in_policies":
                id = helper.AddClockInPolicy(db);
                load = async c => await c.ClockInPolicies.SingleAsync(e => e.Id == id);
                break;
            case "objectives":
                (_, id) = helper.AddProject(db, lead.Id);
                load = async c => await c.Objectives.SingleAsync(e => e.Id == id);
                break;
            case "tasks":
                var (projectId, rootId) = helper.AddProject(db, lead.Id);
                await db.SaveChangesAsync();
                var status = helper.AddStatus(db, projectId, marksComplete: false);
                id = helper.AddTask(db, projectId, rootId, status, progress: 0, due: null).Id;
                load = async c => await c.WorkTasks.SingleAsync(e => e.Id == id);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(table));
        }
        await db.SaveChangesAsync();
        return (helper, load);
    }

    /// <summary>Changes something every one of these entities has.</summary>
    private static void Touch(object entity, int minutes)
    {
        var property = entity.GetType().GetProperty("UpdatedAt")!;
        var when = DateTimeOffset.UtcNow.AddMinutes(minutes);
        property.SetValue(entity, property.PropertyType == typeof(DateTimeOffset) ? when : (DateTimeOffset?)when);
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public async Task The_second_of_two_overlapping_saves_is_rejected(string table)
    {
        var (helper, load) = await SeedAsync(table);
        await using var first = helper.NewTenantContext();
        await using var second = helper.NewTenantContext();
        var mine = await load(first);
        var theirs = await load(second);

        Touch(theirs, 1);
        await second.SaveChangesAsync();
        Touch(mine, 2);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => first.SaveChangesAsync());
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public async Task A_stale_client_version_is_rejected_and_the_current_one_is_accepted(string table)
    {
        var (helper, load) = await SeedAsync(table);
        string staleVersion;
        await using (var read = helper.NewTenantContext())
            staleVersion = new EfRowVersionStore(read).GetVersion(await load(read))!;
        await using (var other = helper.NewTenantContext())
        {
            Touch(await load(other), 1);
            await other.SaveChangesAsync();
        }

        await using (var stale = helper.NewTenantContext())
        {
            var entity = await load(stale);
            Touch(entity, 2);
            new EfRowVersionStore(stale).SetExpectedVersion(entity, staleVersion);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        }

        await using var fresh = helper.NewTenantContext();
        var current = await load(fresh);
        var store = new EfRowVersionStore(fresh);
        var before = store.GetVersion(current);
        Touch(current, 3);
        store.SetExpectedVersion(current, before);
        await fresh.SaveChangesAsync();
        Assert.NotEqual(before, store.GetVersion(current));
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public async Task A_row_loaded_without_tracking_and_then_updated_still_saves(string table)
    {
        var (helper, load) = await SeedAsync(table);
        object detached;
        await using (var read = helper.NewTenantContext())
        {
            detached = await load(read);
            read.Entry(detached).State = EntityState.Detached;
        }

        await using var write = helper.NewTenantContext();
        Touch(detached, 1);
        write.Update(detached);
        await write.SaveChangesAsync(); // ApplicationDbContext reads the current version first (last write wins, as before)
    }
}
