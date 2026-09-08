using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class EfWorkAreaChangeRequestRepositoryTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherTenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid LegalEntityId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OtherLegalEntityId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid EmployeeId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid OtherEmployeeId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private static readonly DateOnly Date = new(2026, 8, 25);

    [Fact]
    public async Task GetApprovedForDate_ReturnsApprovedRowForExactScope()
    {
        await using var db = BuildInMemoryDb();
        var approved = NewRequest(TenantId, LegalEntityId, EmployeeId, Date, WorkAreaChangeRequest.StatusApproved, "remote");
        db.WorkAreaChangeRequests.Add(approved);
        await db.SaveChangesAsync();

        var result = await new EfWorkAreaChangeRequestRepository(db)
            .GetApprovedForDateAsync(TenantId, LegalEntityId, EmployeeId, Date);

        result.Should().NotBeNull();
        result!.Id.Should().Be(approved.Id);
        result.RequestedWorkArea.Should().Be("remote");
    }

    [Theory]
    [InlineData(WorkAreaChangeRequest.StatusPending)]
    [InlineData(WorkAreaChangeRequest.StatusRejected)]
    [InlineData(WorkAreaChangeRequest.StatusCancelled)]
    public async Task GetApprovedForDate_IgnoresNonApprovedStatus(string status)
    {
        await using var db = BuildInMemoryDb();
        db.WorkAreaChangeRequests.Add(
            NewRequest(TenantId, LegalEntityId, EmployeeId, Date, status, "remote"));
        await db.SaveChangesAsync();

        var result = await new EfWorkAreaChangeRequestRepository(db)
            .GetApprovedForDateAsync(TenantId, LegalEntityId, EmployeeId, Date);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetApprovedForDate_IgnoresApprovedRowForAnotherDate()
    {
        await using var db = BuildInMemoryDb();
        db.WorkAreaChangeRequests.Add(
            NewRequest(TenantId, LegalEntityId, EmployeeId, Date.AddDays(1), WorkAreaChangeRequest.StatusApproved, "remote"));
        await db.SaveChangesAsync();

        var result = await new EfWorkAreaChangeRequestRepository(db)
            .GetApprovedForDateAsync(TenantId, LegalEntityId, EmployeeId, Date);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetApprovedForDate_IgnoresApprovedRowForAnotherEmployee()
    {
        await using var db = BuildInMemoryDb();
        db.WorkAreaChangeRequests.Add(
            NewRequest(TenantId, LegalEntityId, OtherEmployeeId, Date, WorkAreaChangeRequest.StatusApproved, "remote"));
        await db.SaveChangesAsync();

        var result = await new EfWorkAreaChangeRequestRepository(db)
            .GetApprovedForDateAsync(TenantId, LegalEntityId, EmployeeId, Date);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetApprovedForDate_IgnoresApprovedRowForAnotherLegalEntity()
    {
        await using var db = BuildInMemoryDb();
        db.WorkAreaChangeRequests.Add(
            NewRequest(TenantId, OtherLegalEntityId, EmployeeId, Date, WorkAreaChangeRequest.StatusApproved, "remote"));
        await db.SaveChangesAsync();

        var result = await new EfWorkAreaChangeRequestRepository(db)
            .GetApprovedForDateAsync(TenantId, LegalEntityId, EmployeeId, Date);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetApprovedForDate_IgnoresApprovedRowForAnotherTenant()
    {
        await using var db = BuildInMemoryDb();
        db.WorkAreaChangeRequests.Add(
            NewRequest(OtherTenantId, LegalEntityId, EmployeeId, Date, WorkAreaChangeRequest.StatusApproved, "remote"));
        await db.SaveChangesAsync();

        var result = await new EfWorkAreaChangeRequestRepository(db)
            .GetApprovedForDateAsync(TenantId, LegalEntityId, EmployeeId, Date);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetApprovedForDate_ReturnsNullWhenNoRowsExist()
    {
        await using var db = BuildInMemoryDb();

        var result = await new EfWorkAreaChangeRequestRepository(db)
            .GetApprovedForDateAsync(TenantId, LegalEntityId, EmployeeId, Date);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetApprovedForDate_ThrowsWhenMoreThanOneApprovedRowExistsForSameScope()
    {
        // The partial unique index should make this impossible in a real database; this proves
        // the repository fails closed rather than arbitrarily picking a row if it ever happens.
        await using var db = BuildInMemoryDb();
        db.WorkAreaChangeRequests.AddRange(
            NewRequest(TenantId, LegalEntityId, EmployeeId, Date, WorkAreaChangeRequest.StatusApproved, "remote"),
            NewRequest(TenantId, LegalEntityId, EmployeeId, Date, WorkAreaChangeRequest.StatusApproved, "onsite"));
        await db.SaveChangesAsync();
        var repository = new EfWorkAreaChangeRequestRepository(db);

        var act = () => repository.GetApprovedForDateAsync(TenantId, LegalEntityId, EmployeeId, Date);

        await act.Should().ThrowAsync<InconsistentWorkAreaChangeRequestStateException>();
    }

    private static WorkAreaChangeRequest NewRequest(
        Guid tenantId, Guid legalEntityId, Guid employeeId, DateOnly date, string status, string requestedWorkArea)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            LegalEntityId = legalEntityId,
            Date = date,
            CurrentExpectedWorkArea = "onsite",
            RequestedWorkArea = requestedWorkArea,
            Reason = "Reason",
            Status = status,
            RequestedAt = new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.Zero)
        };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var currentUser = new Mock<ICurrentUser>();
        var dateTime = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();
        return new ApplicationDbContext(options,
            new AuditableEntityInterceptor(currentUser.Object, dateTime.Object),
            new SoftDeleteInterceptor(dateTime.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
