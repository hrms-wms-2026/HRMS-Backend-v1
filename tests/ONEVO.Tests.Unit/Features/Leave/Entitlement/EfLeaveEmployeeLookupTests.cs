using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Entitlement;

public class EfLeaveEmployeeLookupTests
{
    [Fact]
    public async Task ListActiveByLegalEntityAsync_ReturnsOnlyEmployeesInSelectedLegalEntity()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var selectedLegalEntityId = Guid.NewGuid();
        db.Employees.Add(CreateEmployee(tenantId, selectedLegalEntityId, "EMP-010", "Ravi", "Nadar"));
        db.Employees.Add(CreateEmployee(tenantId, Guid.NewGuid(), "EMP-011", "Tara", "Jones"));
        await db.SaveChangesAsync();

        var repo = new EfEmployeeRepository(db);
        var employees = await repo.ListActiveByLegalEntityAsync(tenantId, selectedLegalEntityId, CancellationToken.None);

        employees.Should().ContainSingle();
        employees[0].EmployeeNumber.Should().Be("EMP-010");
    }

    [Fact]
    public async Task ListLegalEntityChangeWarningsAsync_UsesPositionAssignmentHistory()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var oldLegalEntityId = Guid.NewGuid();
        var newLegalEntityId = Guid.NewGuid();
        var oldPosition = CreatePosition(tenantId, oldLegalEntityId);
        var newPosition = CreatePosition(tenantId, newLegalEntityId);
        db.Employees.Add(CreateEmployee(tenantId, newLegalEntityId, "EMP-012", "Nila", "Perera", employeeId));
        db.Positions.AddRange(oldPosition, newPosition);
        db.PositionAssignments.AddRange(
            CreatePositionAssignment(tenantId, employeeId, oldPosition.Id, new DateOnly(2026, 1, 1), PositionAssignmentStatus.Ended),
            CreatePositionAssignment(tenantId, employeeId, newPosition.Id, new DateOnly(2026, 6, 1), PositionAssignmentStatus.Active));
        await db.SaveChangesAsync();

        var repo = new EfEmployeeRepository(db);
        var warnings = await repo.ListLegalEntityChangeWarningsAsync(tenantId, [employeeId], 2026, CancellationToken.None);

        warnings[employeeId].Should().Be("Employee changed legal entity on 2026-06-01");
    }

    private static Employee CreateEmployee(
        Guid tenantId, Guid legalEntityId, string number, string first, string last, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        TenantId = tenantId,
        UserId = Guid.NewGuid(),
        EmployeeNumber = number,
        FirstName = first,
        LastName = last,
        Email = $"{number}@test.dev",
        LegalEntityId = legalEntityId,
        HireDate = new DateOnly(2024, 1, 1)
    };

    private static Position CreatePosition(Guid tenantId, Guid legalEntityId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = "Role",
        LegalEntityId = legalEntityId,
        IsActive = true
    };

    private static PositionAssignment CreatePositionAssignment(
        Guid tenantId, Guid employeeId, Guid positionId, DateOnly from, string status) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeId = employeeId,
        PositionId = positionId,
        AssignmentKind = PositionAssignmentKind.PrimaryEmployment,
        AssignmentStatus = status,
        EffectiveFrom = from
    };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, new Mock<IDateTimeProvider>().Object),
            new SoftDeleteInterceptor(new Mock<IDateTimeProvider>().Object),
            new DomainEventDispatchInterceptor(new Mock<IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }
}
