using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// The position list's department and reports-to columns sort by the name users see, not by the
/// referenced id. The ids are chosen so id order is the reverse of name order.
/// </summary>
public sealed class PositionSortTests
{
    private static readonly Guid LowId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid HighId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    [Theory]
    [InlineData("asc", new[] { "Alpha", "Zeta", null })]
    [InlineData("desc", new[] { null, "Zeta", "Alpha" })]
    public async Task Positions_SortedByDepartmentName(string direction, string?[] expected)
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        AddDepartment(db, h, LowId, "Zeta");
        AddDepartment(db, h, HighId, "Alpha");
        var inZeta = h.AddPositionHeldBy(db, h.AddEmployee(db).Id);
        var inAlpha = h.AddPositionHeldBy(db, h.AddEmployee(db).Id);
        var none = h.AddPositionHeldBy(db, h.AddEmployee(db).Id);
        await db.SaveChangesAsync();
        (await db.Positions.SingleAsync(p => p.Id == inZeta)).DepartmentId = LowId;
        (await db.Positions.SingleAsync(p => p.Id == inAlpha)).DepartmentId = HighId;
        await db.SaveChangesAsync();
        var names = new Dictionary<Guid, string?> { [inZeta] = "Zeta", [inAlpha] = "Alpha", [none] = null };

        var page = await new EfPositionRepository(db).ListPageAsync(
            h.TenantId, h.LegalEntityId, null, null, true, "department", direction, 1, 50);

        Assert.Equal(expected, page.Items.Where(p => names.ContainsKey(p.Id)).Select(p => names[p.Id]));
    }

    [Theory]
    [InlineData("asc", new[] { "Alpha", "Zeta", null })]
    [InlineData("desc", new[] { null, "Zeta", "Alpha" })]
    public async Task Positions_SortedByReportsToName(string direction, string?[] expected)
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        AddPosition(db, h, LowId, "Zeta");
        AddPosition(db, h, HighId, "Alpha");
        var underZeta = h.AddPositionHeldBy(db, h.AddEmployee(db).Id);
        var underAlpha = h.AddPositionHeldBy(db, h.AddEmployee(db).Id);
        var top = h.AddPositionHeldBy(db, h.AddEmployee(db).Id);
        await db.SaveChangesAsync();
        (await db.Positions.SingleAsync(p => p.Id == underZeta)).ReportsToPositionId = LowId;
        (await db.Positions.SingleAsync(p => p.Id == underAlpha)).ReportsToPositionId = HighId;
        await db.SaveChangesAsync();
        var names = new Dictionary<Guid, string?> { [underZeta] = "Zeta", [underAlpha] = "Alpha", [top] = null };

        var page = await new EfPositionRepository(db).ListPageAsync(
            h.TenantId, h.LegalEntityId, null, null, true, "reportsto", direction, 1, 50);

        Assert.Equal(expected, page.Items.Where(p => names.ContainsKey(p.Id)).Select(p => names[p.Id]));
    }

    private static void AddDepartment(ONEVO.Infrastructure.Persistence.ApplicationDbContext db, MyTeamDb h, Guid id, string name)
        => db.Departments.Add(new Department
        {
            Id = id, TenantId = h.TenantId, LegalEntityId = h.LegalEntityId, Name = name, IsActive = true,
        });

    private static void AddPosition(ONEVO.Infrastructure.Persistence.ApplicationDbContext db, MyTeamDb h, Guid id, string name)
        => db.Positions.Add(new Position
        {
            Id = id, TenantId = h.TenantId, LegalEntityId = h.LegalEntityId, Name = name,
            PositionType = Position.TypeUnique, MaxOccupancy = 1, IsActive = true,
        });
}
