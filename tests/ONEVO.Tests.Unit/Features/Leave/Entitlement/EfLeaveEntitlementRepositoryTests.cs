using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Leave.Entitlement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Entitlement;

public class EfLeaveEntitlementRepositoryTests
{
    [Fact]
    public async Task AddGeneratedAsync_SavesEntitlementsAndAuditInOneCall()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var employee = CreateEmployee(tenantId, Guid.NewGuid(), "EMP-001", "Anu", "Raman");
        var leaveType = CreateLeaveType(tenantId, "Annual Leave", "AL");
        db.Employees.Add(employee);
        db.LeaveTypes.Add(leaveType);
        await db.SaveChangesAsync();

        var entitlement = new LeaveEntitlement
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employee.Id,
            LeaveTypeId = leaveType.Id,
            Year = 2026,
            TotalDays = 17.5m,
            UsedDays = 0m,
            PendingDays = 0m,
            CarriedForwardDays = 2.5m,
            Source = LeaveEntitlementSources.Auto
        };
        var audit = new LeaveBalanceAudit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employee.Id,
            LeaveTypeId = leaveType.Id,
            ChangeType = LeaveBalanceChangeTypes.Accrual,
            DaysChanged = 20m,
            BalanceAfter = 20m,
            Reason = "Generated from active leave policy"
        };

        var repo = new EfLeaveEntitlementRepository(db);
        await repo.AddGeneratedAsync([new LeaveEntitlementWriteSet(entitlement, [audit])], CancellationToken.None);

        (await db.LeaveEntitlements.SingleAsync()).TotalDays.Should().Be(17.5m);
        (await db.LeaveBalanceAudits.SingleAsync()).BalanceAfter.Should().Be(20m);
    }

    [Fact]
    public async Task ListRowsAsync_ComputesRemainingFromTotalCarryUsedAndPending()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var employee = CreateEmployee(tenantId, Guid.NewGuid(), "EMP-002", "Maya", "Silva");
        var leaveType = CreateLeaveType(tenantId, "Study Leave", "ST");
        db.Employees.Add(employee);
        db.LeaveTypes.Add(leaveType);
        db.LeaveEntitlements.Add(new LeaveEntitlement
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employee.Id,
            LeaveTypeId = leaveType.Id,
            Year = 2026,
            TotalDays = 12.5m,
            UsedDays = 4m,
            PendingDays = 1.5m,
            CarriedForwardDays = 2m,
            Source = LeaveEntitlementSources.Manual
        });
        await db.SaveChangesAsync();

        var repo = new EfLeaveEntitlementRepository(db);
        var rows = await repo.ListRowsAsync(
            tenantId,
            new LeaveEntitlementListFilter(2026, null, null, null, null, null, null, null),
            CancellationToken.None);

        rows.Should().ContainSingle();
        rows[0].RemainingDays.Should().Be(9m);
        rows[0].EmployeeName.Should().Be("Maya Silva");
    }

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

    private static Employee CreateEmployee(Guid tenantId, Guid legalEntityId, string number, string first, string last) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = Guid.NewGuid(),
        EmployeeNumber = number,
        FirstName = first,
        LastName = last,
        Email = $"{number}@test.dev",
        LegalEntityId = legalEntityId,
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
}
