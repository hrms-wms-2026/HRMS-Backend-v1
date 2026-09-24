using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public class EfDailyWorkLocationConfirmationRepositoryTests
{
    private static ApplicationDbContext MakeDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<MediatR.IPublisher>();
        var currentUser = new Mock<ICurrentUser>();
        var tenantContext = new Mock<ITenantContext>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }

    [Fact]
    public async Task UpsertAsync_NoExistingRow_Inserts()
    {
        await using var db = MakeDb();
        var repo = new EfDailyWorkLocationConfirmationRepository(db);
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var workDate = new DateOnly(2026, 9, 9);

        await repo.UpsertAsync(new DailyWorkLocationConfirmation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            WorkDate = workDate,
            LocationType = DailyWorkLocationConfirmation.LocationTypeHome,
            Latitude = 6.9271,
            Longitude = 79.8612,
            ConfirmedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var stored = await repo.GetForDateAsync(tenantId, employeeId, workDate);
        Assert.NotNull(stored);
        Assert.Equal(DailyWorkLocationConfirmation.LocationTypeHome, stored!.LocationType);
    }

    [Fact]
    public async Task UpsertAsync_ExistingRowSameDay_UpdatesInPlace()
    {
        await using var db = MakeDb();
        var repo = new EfDailyWorkLocationConfirmationRepository(db);
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var workDate = new DateOnly(2026, 9, 9);
        var firstId = Guid.NewGuid();

        await repo.UpsertAsync(new DailyWorkLocationConfirmation
        {
            Id = firstId, TenantId = tenantId, EmployeeId = employeeId, WorkDate = workDate,
            LocationType = DailyWorkLocationConfirmation.LocationTypeOffice, ConfirmedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        await repo.UpsertAsync(new DailyWorkLocationConfirmation
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, WorkDate = workDate,
            LocationType = DailyWorkLocationConfirmation.LocationTypeHome, ConfirmedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.DailyWorkLocationConfirmations.CountAsync());
        var stored = await repo.GetForDateAsync(tenantId, employeeId, workDate);
        Assert.Equal(firstId, stored!.Id);
        Assert.Equal(DailyWorkLocationConfirmation.LocationTypeHome, stored.LocationType);
    }
}
