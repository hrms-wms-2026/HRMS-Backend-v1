using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Leave.Cancellation;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Cancellation;

public class EfLeaveCancellationRepositoryTests
{
    [Fact]
    public async Task GetStateAsync_LoadsTrackedRequestAndApprovers()
    {
        await using var db = BuildDb();
        var tenantId = Guid.NewGuid();
        var legalEntity = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = "Acme", Timezone = "Asia/Colombo",
            CountryCode = "LKA", CurrencyCode = "LKR"
        };
        var employee = new Employee
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = Guid.NewGuid(), FirstName = "Priya", LastName = "Nair",
            EmployeeNumber = "E1", HireDate = new DateOnly(2024, 1, 1), LegalEntityId = legalEntity.Id
        };
        var leaveType = new LeaveType { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Annual Leave", Code = "AL" };
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id, LeaveTypeId = leaveType.Id,
            StartDate = new DateOnly(2026, 9, 14), EndDate = new DateOnly(2026, 9, 14), TotalDays = 1m, PaidDays = 1m,
            Status = LeaveRequestStatuses.Pending
        };
        var approver = new LeaveRequestApprover
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LeaveRequestId = request.Id, ApproverEmployeeId = Guid.NewGuid(),
            SequenceOrder = 1, Status = LeaveRequestApproverStatuses.Pending
        };
        db.LegalEntities.Add(legalEntity);
        db.Employees.Add(employee);
        db.LeaveTypes.Add(leaveType);
        db.LeaveRequests.Add(request);
        db.LeaveRequestApprovers.Add(approver);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfLeaveCancellationRepository(db);
        var state = await repo.GetStateAsync(tenantId, request.Id, CancellationToken.None);

        state.Should().NotBeNull();
        db.Entry(state!.Request).State.Should().Be(EntityState.Unchanged);
        db.Entry(state.Approvers.Single()).State.Should().Be(EntityState.Unchanged);
        state.LegalEntity!.Timezone.Should().Be("Asia/Colombo");
    }

    [Fact]
    public async Task ListAllocationsAsync_ReturnsRowsOrderedByDate()
    {
        await using var db = BuildDb();
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        db.LeaveRequestDayAllocations.AddRange(
            new LeaveRequestDayAllocation
            {
                Id = Guid.NewGuid(), TenantId = tenantId, LeaveRequestId = requestId,
                LeaveDate = new DateOnly(2026, 9, 16), DayUnit = 1m, Status = LeaveRequestDayAllocationStatuses.Active
            },
            new LeaveRequestDayAllocation
            {
                Id = Guid.NewGuid(), TenantId = tenantId, LeaveRequestId = requestId,
                LeaveDate = new DateOnly(2026, 9, 14), DayUnit = 1m, Status = LeaveRequestDayAllocationStatuses.Cancelled
            });
        await db.SaveChangesAsync();

        var rows = await new EfLeaveCancellationRepository(db).ListAllocationsAsync(tenantId, requestId, CancellationToken.None);

        rows.Select(x => x.LeaveDate).Should().Equal(new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 16));
    }

    [Fact]
    public void SetExpectedVersion_SetsXminWhenNumeric()
    {
        using var db = BuildDb();
        var request = new LeaveRequest { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Status = LeaveRequestStatuses.Pending };
        db.LeaveRequests.Attach(request);
        var repo = new EfLeaveCancellationRepository(db);

        repo.SetExpectedVersion(request, "42");

        db.Entry(request).Property("xmin").OriginalValue.Should().Be(42u);
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
}
