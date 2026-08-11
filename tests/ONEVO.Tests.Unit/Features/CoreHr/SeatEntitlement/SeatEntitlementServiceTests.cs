using Microsoft.EntityFrameworkCore;
using MediatR;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Services.CoreHr.SeatEntitlement;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.SeatEntitlement;

public sealed class SeatEntitlementServiceTests
{
    [Fact]
    public async Task EvaluateAsync_ReturnsUndetermined_WhenNoSubscriptionRecordExists()
    {
        await using var db = BuildInMemoryDb();
        var service = new SeatEntitlementService(db);

        var decision = await service.EvaluateAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(SeatDecisionStatus.Undetermined, decision.Status);
        Assert.Null(decision.PurchasedSeats);
        Assert.Null(decision.AvailableSeats);
        Assert.False(decision.RequestSeatIncreaseAvailable);
    }

    [Fact]
    public async Task EvaluateAsync_ReturnsUndetermined_WhenSubscriptionPolicyIsIncomplete()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        db.Set<TenantSubscription>().Add(NewSubscription(tenantId));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = new SeatEntitlementService(db);
        var decision = await service.EvaluateAsync(tenantId, CancellationToken.None);

        Assert.Equal(SeatDecisionStatus.Undetermined, decision.Status);
        Assert.Null(decision.PurchasedSeats);
        Assert.True(decision.RequestSeatIncreaseAvailable);
    }

    [Fact]
    public async Task EvaluateAsync_ReturnsApproved_WhenIncludedCapacityExists()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        db.Set<TenantSubscription>().Add(NewSubscription(tenantId, includedSeats: 2, overageAllowed: false));
        db.Employees.Add(NewEmployee(tenantId));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var decision = await new SeatEntitlementService(db).EvaluateAsync(tenantId, CancellationToken.None);

        Assert.Equal(SeatDecisionStatus.Approved, decision.Status);
    }

    [Fact]
    public async Task EvaluateAsync_CountsActiveEmployeesForTheGivenTenantOnly()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        db.Employees.AddRange(NewEmployee(tenantId), NewEmployee(tenantId), NewEmployee(otherTenantId));
        db.Set<TenantSubscription>().Add(NewSubscription(tenantId));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var decision = await new SeatEntitlementService(db).EvaluateAsync(tenantId, CancellationToken.None);

        Assert.Equal(2, decision.ActiveEmployeeCount);
    }

    [Fact]
    public async Task EvaluateAsync_ReturnsBlocked_WhenCapacityIsExhaustedAndOverageIsDisabled()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        db.Employees.Add(NewEmployee(tenantId));
        db.Set<TenantSubscription>().Add(NewSubscription(tenantId, includedSeats: 1, overageAllowed: false));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var decision = await new SeatEntitlementService(db).EvaluateAsync(tenantId, CancellationToken.None);

        Assert.Equal(SeatDecisionStatus.Blocked, decision.Status);
        Assert.Equal(0, decision.PendingReservedSeats);
    }

    [Fact]
    public async Task EvaluateAsync_ReturnsApproved_WhenCapacityIsExhaustedAndOverageIsAllowed()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        db.Employees.Add(NewEmployee(tenantId));
        db.Set<TenantSubscription>().Add(NewSubscription(tenantId, includedSeats: 1, overageAllowed: true));
        await db.SaveChangesAsync();

        var decision = await new SeatEntitlementService(db).EvaluateAsync(tenantId, CancellationToken.None);

        Assert.Equal(SeatDecisionStatus.Approved, decision.Status);
        Assert.True(decision.OverageAllowed);
    }

    private static TenantSubscription NewSubscription(Guid tenantId, int? includedSeats = null, bool? overageAllowed = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        PlanId = Guid.NewGuid(),
        CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow),
        CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
        ContractStartDate = DateOnly.FromDateTime(DateTime.UtcNow),
        CompanySizeRange = "51-200",
        IncludedSeats = includedSeats,
        OverageAllowed = overageAllowed,
    };

    private static EmployeeEntity NewEmployee(Guid tenantId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = Guid.NewGuid(),
        EmployeeNumber = Guid.NewGuid().ToString("N")[..8],
        FirstName = "Test",
        LastName = "Employee",
        Email = $"{Guid.NewGuid():N}@test.dev",
        HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
    };

    private static OnboardingDraft NewDraft(Guid tenantId, string status) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        FirstName = "Pending",
        LastName = "Hire",
        WorkEmail = $"{Guid.NewGuid():N}@test.dev",
        LegalEntityId = Guid.NewGuid(),
        EmploymentType = "full_time",
        WorkModeId = 1,
        StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Status = status,
        StartedById = Guid.NewGuid(),
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
