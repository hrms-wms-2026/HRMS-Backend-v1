using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Leave.Request;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Request;

public class SubmitLeaveRequestCommandHandlerTests
{
    [Fact]
    public async Task AddPendingRequestAsync_PersistsThreeActiveAllocations()
    {
        await using var db = BuildDb();
        var tenantId = Guid.NewGuid();
        var request = Request(tenantId, 3m);
        var entitlement = Entitlement(tenantId, request, pending: 0m);
        db.LeaveEntitlements.Add(entitlement);
        await db.SaveChangesAsync();

        var allocations = new[]
        {
            Allocation(tenantId, request.Id, new DateOnly(2026, 9, 14), 1m),
            Allocation(tenantId, request.Id, new DateOnly(2026, 9, 15), 1m),
            Allocation(tenantId, request.Id, new DateOnly(2026, 9, 16), 1m)
        };
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(DateTimeOffset.UtcNow);
        var repo = new EfLeaveRequestRepository(db, clock.Object);

        await repo.AddPendingRequestAsync(new LeaveRequestWriteSet(request, [], [], allocations, entitlement), CancellationToken.None);

        var saved = await db.LeaveRequestDayAllocations.Where(x => x.LeaveRequestId == request.Id).ToListAsync();
        saved.Should().HaveCount(3);
        saved.Sum(x => x.PaidUnit).Should().Be(3m);
        saved.Should().OnlyContain(x => x.Status == LeaveRequestDayAllocationStatuses.Active);
    }

    [Fact]
    public async Task AddPendingRequestAsync_HalfDayWritesPointFiveUnit()
    {
        await using var db = BuildDb();
        var tenantId = Guid.NewGuid();
        var request = Request(tenantId, 0.5m);
        var entitlement = Entitlement(tenantId, request, pending: 0m);
        db.LeaveEntitlements.Add(entitlement);
        await db.SaveChangesAsync();
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(DateTimeOffset.UtcNow);
        var repo = new EfLeaveRequestRepository(db, clock.Object);

        await repo.AddPendingRequestAsync(new LeaveRequestWriteSet(
            request, [], [],
            [Allocation(tenantId, request.Id, request.StartDate, 0.5m)],
            entitlement), CancellationToken.None);

        var saved = await db.LeaveRequestDayAllocations.SingleAsync();
        saved.DayUnit.Should().Be(0.5m);
    }

    [Fact]
    public async Task AddPendingRequestAsync_OverlapDoesNotCommitAllocations()
    {
        await using var db = BuildDb();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var existing = Request(tenantId, 1m);
        existing.EmployeeId = employeeId;
        existing.Status = LeaveRequestStatuses.Pending;
        db.LeaveRequests.Add(existing);
        var entitlement = Entitlement(tenantId, existing, pending: 1m);
        db.LeaveEntitlements.Add(entitlement);
        await db.SaveChangesAsync();

        var overlapping = Request(tenantId, 1m);
        overlapping.EmployeeId = employeeId;
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(DateTimeOffset.UtcNow);
        var repo = new EfLeaveRequestRepository(db, clock.Object);

        var act = () => repo.AddPendingRequestAsync(new LeaveRequestWriteSet(
            overlapping, [], [],
            [Allocation(tenantId, overlapping.Id, overlapping.StartDate, 1m)],
            entitlement), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await db.LeaveRequestDayAllocations.CountAsync()).Should().Be(0);
    }

    private static ApplicationDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, new Mock<IDateTimeProvider>().Object),
            new SoftDeleteInterceptor(new Mock<IDateTimeProvider>().Object),
            new DomainEventDispatchInterceptor(new Mock<IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }

    private static LeaveRequest Request(Guid tenantId, decimal days) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeId = Guid.NewGuid(),
        LeaveTypeId = Guid.NewGuid(),
        StartDate = new DateOnly(2026, 9, 14),
        EndDate = new DateOnly(2026, 9, 14),
        TotalDays = days,
        PaidDays = days,
        Status = LeaveRequestStatuses.Pending
    };

    private static LeaveEntitlement Entitlement(Guid tenantId, LeaveRequest request, decimal pending) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeId = request.EmployeeId,
        LeaveTypeId = request.LeaveTypeId,
        Year = 2026,
        TotalDays = 20m,
        PendingDays = pending,
        Source = LeaveEntitlementSources.Auto
    };

    private static LeaveRequestDayAllocation Allocation(Guid tenantId, Guid requestId, DateOnly date, decimal unit) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        LeaveRequestId = requestId,
        LeaveDate = date,
        DayUnit = unit,
        PaidUnit = unit,
        UnpaidUnit = 0m,
        Status = LeaveRequestDayAllocationStatuses.Active
    };
}
