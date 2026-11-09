using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// PostgreSQL sorts NULLs last ascending and first descending; SQL Server does the opposite. Lists
/// users can sort must keep the PostgreSQL order.
/// </summary>
public sealed class NullSortOrderTests
{
    [Theory]
    [InlineData(SortDirection.Ascending, new[] { "A-1", null })]
    [InlineData(SortDirection.Descending, new[] { null, "A-1" })]
    public async Task Departments_SortedByCode(SortDirection direction, string?[] expected)
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        h.AddDepartment(db);
        var coded = await SaveAndFindAsync(db, h.AddDepartment(db));
        coded.Code = "A-1";
        await db.SaveChangesAsync();

        var page = await new EfDepartmentRepository(db).ListPageByLegalEntityAsync(
            h.TenantId, h.LegalEntityId, null, true, null, DepartmentSortBy.Code, direction, 1, 10);

        Assert.Equal(expected, page.Items.Select(d => d.Code));
    }

    [Theory]
    [InlineData("asc", new[] { "P-1", null })]
    [InlineData("desc", new[] { null, "P-1" })]
    public async Task Positions_SortedByCode(string direction, string?[] expected)
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        h.AddPositionHeldBy(db, h.AddEmployee(db).Id);
        var codedId = h.AddPositionHeldBy(db, h.AddEmployee(db).Id);
        await db.SaveChangesAsync();
        (await db.Positions.SingleAsync(p => p.Id == codedId)).Code = "P-1";
        await db.SaveChangesAsync();

        var page = await new EfPositionRepository(db).ListPageAsync(
            h.TenantId, h.LegalEntityId, null, null, true, "code", direction, 1, 10);

        Assert.Equal(expected, page.Items.Where(p => p.Code is "P-1" or null).Select(p => p.Code));
    }

    [Theory]
    [InlineData(false, new[] { true, false })]
    [InlineData(true, new[] { false, true })]
    public async Task Employees_SortedByDepartment(bool descending, bool[] expectedHasDepartment)
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var withDepartment = h.AddEmployee(db, h.AddDepartment(db));
        var withoutDepartment = h.AddEmployee(db);
        await db.SaveChangesAsync();
        var scope = new EmployeeVisibilityScope(true, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        var filter = new EmployeeListFilter(null, null, null, [withDepartment.Id, withoutDepartment.Id],
            SortBy: "department", SortDescending: descending);

        var (items, _) = await new EfEmployeeRepository(db).ListVisibleAsync(h.TenantId, scope, filter, 1, 10);

        Assert.Equal(expectedHasDepartment, items.Select(i => i.Id == withDepartment.Id));
    }

    [Fact]
    public async Task ChecklistTasks_WithoutASequence_ComeLast()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var employee = h.AddEmployee(db);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(TestDb.DisableForeignKeysSql("employee_checklist_tasks"));
        db.EmployeeChecklistTasks.AddRange(ChecklistTask(h, employee.Id, null), ChecklistTask(h, employee.Id, 1));
        await db.SaveChangesAsync();

        var tasks = await new EfEmployeeChecklistTaskRepository(db).ListByEmployeeAsync(h.TenantId, employee.Id);

        Assert.Equal(new int?[] { 1, null }, tasks.Select(t => t.Sequence));
    }

    private static EmployeeChecklistTask ChecklistTask(MyTeamDb h, Guid employeeId, int? sequence) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = employeeId, LifecycleType = "onboarding",
        TaskTitle = "Task", OwnerType = "hr", AssignedToId = employeeId,
        DueDate = DateOnly.FromDateTime(DateTime.UtcNow), Sequence = sequence,
    };

    private static async Task<ONEVO.Domain.Features.OrgStructure.Entities.Department> SaveAndFindAsync(
        ONEVO.Infrastructure.Persistence.ApplicationDbContext db, Guid id)
    {
        await db.SaveChangesAsync();
        return await db.Departments.SingleAsync(d => d.Id == id);
    }
}
