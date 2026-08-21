using Microsoft.EntityFrameworkCore;
using MediatR;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class EfEmployeeRepositoryTests
{
    [Fact]
    public async Task ListVisibleAsync_ReturnsAllTenantEmployees_WhenScopeIsUnrestricted()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        db.Employees.AddRange(NewEmployee(tenantId, "E-001"), NewEmployee(tenantId, "E-002"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfEmployeeRepository(db);
        var (items, total) = await repo.ListVisibleAsync(
            tenantId, EmployeeVisibilityScope.Unrestricted(), new EmployeeListFilter(null, null, null), 1, 25, CancellationToken.None);

        Assert.Equal(2, total);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task ListVisibleAsync_ExcludesEmployeesOutsideCoverage_WhenScopeIsRestrictedByDepartment()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var coveredDeptId = Guid.NewGuid();
        var otherDeptId = Guid.NewGuid();

        var inCoverage = NewEmployee(tenantId, "E-001");
        inCoverage.DepartmentId = coveredDeptId;
        var outOfCoverage = NewEmployee(tenantId, "E-002");
        outOfCoverage.DepartmentId = otherDeptId;

        db.Employees.AddRange(inCoverage, outOfCoverage);
        db.Departments.AddRange(
            new Department { Id = coveredDeptId, TenantId = tenantId, Name = "Covered", LegalEntityId = Guid.NewGuid() },
            new Department { Id = otherDeptId, TenantId = tenantId, Name = "Other", LegalEntityId = Guid.NewGuid() });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var scope = new EmployeeVisibilityScope(
            false, null, new HashSet<Guid>(), new HashSet<Guid> { coveredDeptId }, new HashSet<Guid>());

        var repo = new EfEmployeeRepository(db);
        var (items, total) = await repo.ListVisibleAsync(
            tenantId, scope, new EmployeeListFilter(null, null, null), 1, 25, CancellationToken.None);

        Assert.Equal(1, total);
        Assert.Equal(inCoverage.Id, items[0].Id);
    }

    [Fact]
    public async Task ListVisibleAsync_AlwaysIncludesCallersOwnEmployeeRow_EvenOutsideCoverage()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var self = NewEmployee(tenantId, "E-001");
        db.Employees.Add(self);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var scope = new EmployeeVisibilityScope(
            false, self.Id, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

        var repo = new EfEmployeeRepository(db);
        var (items, total) = await repo.ListVisibleAsync(
            tenantId, scope, new EmployeeListFilter(null, null, null), 1, 25, CancellationToken.None);

        Assert.Equal(1, total);
        Assert.Equal(self.Id, items[0].Id);
    }

    [Fact]
    public async Task ListVisibleAsync_RestrictsToGivenIds_IgnoringScope_WhenRestrictToEmployeeIdsIsSet()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var allowed = NewEmployee(tenantId, "E-001");
        var notAllowed = NewEmployee(tenantId, "E-002");
        db.Employees.AddRange(allowed, notAllowed);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfEmployeeRepository(db);
        var (items, total) = await repo.ListVisibleAsync(
            tenantId,
            EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter(null, null, null, new HashSet<Guid> { allowed.Id }),
            1, 25, CancellationToken.None);

        Assert.Equal(1, total);
        Assert.Equal(allowed.Id, items[0].Id);
    }

    [Fact]
    public async Task ListVisibleAsync_ReturnsEmpty_WhenRestrictToEmployeeIdsIsEmptySet()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        db.Employees.Add(NewEmployee(tenantId, "E-001"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfEmployeeRepository(db);
        var (items, total) = await repo.ListVisibleAsync(
            tenantId,
            EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter(null, null, null, new HashSet<Guid>()),
            1, 25, CancellationToken.None);

        Assert.Equal(0, total);
        Assert.Empty(items);
    }

    [Fact]
    public async Task ListVisibleAsync_AppliesSearchFilter_WithinRestrictToEmployeeIds()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        // Explicit non-colliding emails: NewEmployee's default random email is a hex GUID
        // string, which has a small but real chance of containing "ada" as a substring
        // (a/d are valid hex digits) and flakily matching Bob too.
        var ada = NewEmployee(tenantId, "E-001", email: "ada@test.dev");
        ada.FirstName = "Ada";
        var bob = NewEmployee(tenantId, "E-002", email: "bob@test.dev");
        bob.FirstName = "Bob";
        db.Employees.AddRange(ada, bob);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfEmployeeRepository(db);
        var (items, total) = await repo.ListVisibleAsync(
            tenantId,
            EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter("ada", null, null, new HashSet<Guid> { ada.Id, bob.Id }),
            1, 25, CancellationToken.None);

        Assert.Equal(1, total);
        Assert.Equal(ada.Id, items[0].Id);
    }

    [Fact]
    public async Task ListVisibleAsync_ResolvesReportingManagerFromHierarchyClosure()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var manager = NewEmployee(tenantId, "MGR-001");
        var report = NewEmployee(tenantId, "E-001");
        db.Employees.AddRange(manager, report);
        db.EmployeeHierarchyClosures.Add(new ONEVO.Domain.Features.CoreHr.Entities.EmployeeHierarchyClosure
        {
            TenantId = tenantId,
            AncestorEmployeeId = manager.Id,
            DescendantEmployeeId = report.Id,
            Depth = 1,
            SourcePositionAssignmentId = Guid.NewGuid(),
            GeneratedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfEmployeeRepository(db);
        var result = await repo.GetVisibleByIdAsync(tenantId, EmployeeVisibilityScope.Unrestricted(), report.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(manager.Id, result!.ReportingManagerId);
        Assert.Equal($"{manager.FirstName} {manager.LastName}", result.ReportingManagerName);
    }

    [Fact]
    public async Task ListVisibleAsync_LeavesReportingManagerNull_WhenNoClosureRowExists()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfEmployeeRepository(db);
        var result = await repo.GetVisibleByIdAsync(tenantId, EmployeeVisibilityScope.Unrestricted(), employee.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result!.ReportingManagerId);
    }

    [Fact]
    public async Task EmailExistsAsync_IsCaseInsensitive_AndTenantScoped()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        employee.Email = "Ada@Test.Dev";
        db.Employees.Add(employee);
        db.Employees.Add(NewEmployee(otherTenantId, "E-001-B", email: "shared@test.dev"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfEmployeeRepository(db);

        Assert.True(await repo.EmailExistsAsync(tenantId, "ada@test.dev", null, CancellationToken.None));
        Assert.False(await repo.EmailExistsAsync(otherTenantId, "ada@test.dev", null, CancellationToken.None));
    }

    [Fact]
    public async Task EmailExistsAsync_ExcludesGivenId()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfEmployeeRepository(db);
        var exists = await repo.EmailExistsAsync(tenantId, employee.Email, employee.Id, CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task EmployeeNumberExistsAsync_IsTenantScoped()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        db.Employees.Add(NewEmployee(tenantId, "SHARED-001"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfEmployeeRepository(db);

        Assert.True(await repo.EmployeeNumberExistsAsync(tenantId, "SHARED-001", null, CancellationToken.None));
        Assert.False(await repo.EmployeeNumberExistsAsync(otherTenantId, "SHARED-001", null, CancellationToken.None));
    }

    private static EmployeeEntity NewEmployee(Guid tenantId, string employeeNumber, string? email = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = Guid.NewGuid(),
        EmployeeNumber = employeeNumber,
        FirstName = "Test",
        LastName = employeeNumber,
        Email = email ?? $"{Guid.NewGuid():N}@test.dev",
        HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
    };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();

        var auditInterceptor = new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object);
        var softDeleteInterceptor = new SoftDeleteInterceptor(dateTimeProvider.Object);
        var domainEventInterceptor = new DomainEventDispatchInterceptor(publisher.Object);

        return new ApplicationDbContext(
            optionsBuilder.Options,
            auditInterceptor,
            softDeleteInterceptor,
            domainEventInterceptor,
            tenantContext.Object);
    }
}
