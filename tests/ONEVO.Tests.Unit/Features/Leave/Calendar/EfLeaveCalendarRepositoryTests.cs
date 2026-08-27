using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.Leave.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Leave.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Calendar;

public class EfLeaveCalendarRepositoryTests
{
    [Fact]
    public async Task ListMonthRequestsAsync_ReturnsApprovedRequestsOverlappingMonth()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntity = CreateLegalEntity(tenantId, "Acme Lanka");
        var department = CreateDepartment(tenantId, legalEntity.Id, "Engineering");
        var employee = CreateEmployee(tenantId, legalEntity.Id, department.Id, "EMP-001", "Priya", "Nair");
        var leaveType = CreateLeaveType(tenantId, "Annual Leave", "AL");
        db.AddRange(legalEntity, department, employee, leaveType);
        db.LeaveRequests.Add(CreateRequest(tenantId, employee.Id, leaveType.Id, LeaveRequestStatuses.Approved, new DateOnly(2026, 7, 30), new DateOnly(2026, 8, 2)));
        await db.SaveChangesAsync();

        var repo = new EfLeaveCalendarRepository(db);
        var rows = await repo.ListMonthRequestsAsync(
            tenantId,
            EmployeeVisibilityScope.Unrestricted(),
            Filter(includeTentative: true),
            CancellationToken.None);

        rows.Should().ContainSingle();
        rows[0].EmployeeName.Should().Be("Priya Nair");
        rows[0].DepartmentName.Should().Be("Engineering");
        rows[0].LegalEntityName.Should().Be("Acme Lanka");
        rows[0].LeaveTypeCategory.Should().Be(LeaveTypeCategories.Annual);
    }

    [Fact]
    public async Task ListMonthRequestsAsync_ExcludesPending_WhenTentativeBlocksDisabled()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var setup = await SeedEmployeeAndTypeAsync(db, tenantId);
        db.LeaveRequests.Add(CreateRequest(tenantId, setup.Employee.Id, setup.LeaveType.Id, LeaveRequestStatuses.Pending, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 10)));
        await db.SaveChangesAsync();

        var repo = new EfLeaveCalendarRepository(db);
        var hidden = await repo.ListMonthRequestsAsync(tenantId, EmployeeVisibilityScope.Unrestricted(), Filter(includeTentative: false), CancellationToken.None);
        var visible = await repo.ListMonthRequestsAsync(tenantId, EmployeeVisibilityScope.Unrestricted(), Filter(includeTentative: true), CancellationToken.None);

        hidden.Should().BeEmpty();
        visible.Should().ContainSingle(row => row.Request.Status == LeaveRequestStatuses.Pending);
    }

    [Fact]
    public async Task ListMonthRequestsAsync_ExcludesRejectedAndFullyCancelledRequests()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var setup = await SeedEmployeeAndTypeAsync(db, tenantId);
        db.LeaveRequests.AddRange(
            CreateRequest(tenantId, setup.Employee.Id, setup.LeaveType.Id, LeaveRequestStatuses.Rejected, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 10)),
            CreateRequest(tenantId, setup.Employee.Id, setup.LeaveType.Id, LeaveRequestStatuses.Cancelled, new DateOnly(2026, 8, 12), new DateOnly(2026, 8, 12)));
        await db.SaveChangesAsync();

        var repo = new EfLeaveCalendarRepository(db);
        var rows = await repo.ListMonthRequestsAsync(tenantId, EmployeeVisibilityScope.Unrestricted(), Filter(includeTentative: true), CancellationToken.None);

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ListMonthRequestsAsync_ReturnsPartialCancellationForProjectorClipping()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var setup = await SeedEmployeeAndTypeAsync(db, tenantId);
        var request = CreateRequest(tenantId, setup.Employee.Id, setup.LeaveType.Id, LeaveRequestStatuses.Cancelled, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 14));
        request.PartialCancelEffectiveDate = new DateOnly(2026, 8, 13);
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();

        var repo = new EfLeaveCalendarRepository(db);
        var rows = await repo.ListMonthRequestsAsync(tenantId, EmployeeVisibilityScope.Unrestricted(), Filter(includeTentative: true), CancellationToken.None);

        rows.Should().ContainSingle(row => row.Request.Id == request.Id);
    }

    [Fact]
    public async Task ListMonthRequestsAsync_AppliesDepartmentFilter()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntity = CreateLegalEntity(tenantId, "Acme Lanka");
        var engineering = CreateDepartment(tenantId, legalEntity.Id, "Engineering");
        var sales = CreateDepartment(tenantId, legalEntity.Id, "Sales");
        var leaveType = CreateLeaveType(tenantId, "Annual Leave", "AL");
        var engineer = CreateEmployee(tenantId, legalEntity.Id, engineering.Id, "EMP-002", "Anu", "Raman");
        var seller = CreateEmployee(tenantId, legalEntity.Id, sales.Id, "EMP-003", "Maya", "Silva");
        db.AddRange(legalEntity, engineering, sales, leaveType, engineer, seller);
        db.LeaveRequests.AddRange(
            CreateRequest(tenantId, engineer.Id, leaveType.Id, LeaveRequestStatuses.Approved, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 10)),
            CreateRequest(tenantId, seller.Id, leaveType.Id, LeaveRequestStatuses.Approved, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 10)));
        await db.SaveChangesAsync();

        var repo = new EfLeaveCalendarRepository(db);
        var rows = await repo.ListMonthRequestsAsync(
            tenantId,
            EmployeeVisibilityScope.Unrestricted(),
            Filter(includeTentative: true, departmentId: engineering.Id),
            CancellationToken.None);

        rows.Should().ContainSingle(row => row.EmployeeName == "Anu Raman");
    }

    [Fact]
    public async Task ListMonthRequestsAsync_AppliesVisibilityScope()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntity = CreateLegalEntity(tenantId, "Acme Lanka");
        var ownDepartment = CreateDepartment(tenantId, legalEntity.Id, "Engineering");
        var coveredDepartment = CreateDepartment(tenantId, legalEntity.Id, "Operations");
        var unrelatedDepartment = CreateDepartment(tenantId, legalEntity.Id, "Sales");
        var leaveType = CreateLeaveType(tenantId, "Annual Leave", "AL");
        var own = CreateEmployee(tenantId, legalEntity.Id, ownDepartment.Id, "EMP-004", "Priya", "Nair");
        var covered = CreateEmployee(tenantId, legalEntity.Id, coveredDepartment.Id, "EMP-005", "Ravi", "Kumar");
        var unrelated = CreateEmployee(tenantId, legalEntity.Id, unrelatedDepartment.Id, "EMP-006", "Tara", "Jones");
        db.AddRange(legalEntity, ownDepartment, coveredDepartment, unrelatedDepartment, leaveType, own, covered, unrelated);
        db.LeaveRequests.AddRange(
            CreateRequest(tenantId, own.Id, leaveType.Id, LeaveRequestStatuses.Approved, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 10)),
            CreateRequest(tenantId, covered.Id, leaveType.Id, LeaveRequestStatuses.Approved, new DateOnly(2026, 8, 11), new DateOnly(2026, 8, 11)),
            CreateRequest(tenantId, unrelated.Id, leaveType.Id, LeaveRequestStatuses.Approved, new DateOnly(2026, 8, 12), new DateOnly(2026, 8, 12)));
        await db.SaveChangesAsync();

        var scope = new EmployeeVisibilityScope(
            false,
            own.Id,
            new HashSet<Guid>(),
            new HashSet<Guid> { coveredDepartment.Id },
            new HashSet<Guid>());

        var repo = new EfLeaveCalendarRepository(db);
        var rows = await repo.ListMonthRequestsAsync(tenantId, scope, Filter(includeTentative: true), CancellationToken.None);

        rows.Select(row => row.EmployeeName).Should().BeEquivalentTo("Priya Nair", "Ravi Kumar");
    }

    private static async Task<(Employee Employee, LeaveType LeaveType)> SeedEmployeeAndTypeAsync(ApplicationDbContext db, Guid tenantId)
    {
        var legalEntity = CreateLegalEntity(tenantId, "Acme Lanka");
        var department = CreateDepartment(tenantId, legalEntity.Id, "Engineering");
        var employee = CreateEmployee(tenantId, legalEntity.Id, department.Id, "EMP-001", "Priya", "Nair");
        var leaveType = CreateLeaveType(tenantId, "Annual Leave", "AL");
        db.AddRange(legalEntity, department, employee, leaveType);
        await db.SaveChangesAsync();
        return (employee, leaveType);
    }

    private static LeaveCalendarRequestFilter Filter(bool includeTentative, Guid? departmentId = null) =>
        new(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), departmentId, includeTentative);

    private static LeaveRequest CreateRequest(Guid tenantId, Guid employeeId, Guid leaveTypeId, string status, DateOnly start, DateOnly end) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeId = employeeId,
        LeaveTypeId = leaveTypeId,
        StartDate = start,
        EndDate = end,
        Status = status,
        TotalDays = end.DayNumber - start.DayNumber + 1,
        PaidDays = end.DayNumber - start.DayNumber + 1
    };

    private static Employee CreateEmployee(Guid tenantId, Guid legalEntityId, Guid departmentId, string number, string first, string last) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = Guid.NewGuid(),
        EmployeeNumber = number,
        FirstName = first,
        LastName = last,
        Email = $"{number}@test.dev",
        LegalEntityId = legalEntityId,
        DepartmentId = departmentId,
        HireDate = new DateOnly(2024, 1, 1)
    };

    private static LeaveType CreateLeaveType(Guid tenantId, string name, string code) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = name,
        Code = code,
        Category = LeaveTypeCategories.Annual,
        DefaultDaysPerYear = 20m,
        IsActive = true
    };

    private static Department CreateDepartment(Guid tenantId, Guid legalEntityId, string name) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        LegalEntityId = legalEntityId,
        Name = name
    };

    private static LegalEntity CreateLegalEntity(Guid tenantId, string name) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = name,
        CountryCode = "LK",
        CurrencyCode = "LKR"
    };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, new Mock<IDateTimeProvider>().Object),
            new SoftDeleteInterceptor(new Mock<IDateTimeProvider>().Object),
            new DomainEventDispatchInterceptor(new Mock<IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }
}
