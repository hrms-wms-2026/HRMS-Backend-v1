using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.WorkSessions.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.WorkSessions;

namespace ONEVO.Tests.Unit.Features.Monitoring.WorkSessions;

public sealed class EfWorkSessionRepositoryTests
{
    [Fact]
    public async Task GetByEmployeeRangeAsync_resolves_employee_id_to_user_id()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var matchingSessionId = Guid.NewGuid();
        var straySessionId = Guid.NewGuid();

        await using var db = BuildDb();
        db.Employees.Add(new Employee
        {
            Id = employeeId,
            TenantId = tenantId,
            UserId = userId,
            EmployeeNumber = "EMP-001",
            FirstName = "Pirakee",
            LastName = "Dev",
            Email = "pirakee@example.com",
            EmploymentTypeId = 1,
            EmploymentStatusId = 1,
            WorkModeId = 1,
            HireDate = new DateOnly(2026, 1, 1)
        });
        db.EmployeeWorkSessions.AddRange(
            new EmployeeWorkSession
            {
                Id = matchingSessionId,
                TenantId = tenantId,
                UserId = userId,
                ClockInAt = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero),
                ClockOutAt = new DateTimeOffset(2026, 8, 14, 18, 0, 0, TimeSpan.Zero)
            },
            new EmployeeWorkSession
            {
                Id = straySessionId,
                TenantId = tenantId,
                UserId = employeeId,
                ClockInAt = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero),
                ClockOutAt = new DateTimeOffset(2026, 8, 14, 18, 0, 0, TimeSpan.Zero)
            });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfWorkSessionRepository(db);

        var sessions = await repository.GetByEmployeeRangeAsync(
            tenantId,
            employeeId,
            new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        sessions.Should().ContainSingle()
            .Which.Id.Should().Be(matchingSessionId);
    }

    private static ApplicationDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var clock = new Mock<IDateTimeProvider>();
        var currentUser = new Mock<ICurrentUser>();
        var publisher = new Mock<IPublisher>();
        var tenant = new Mock<ITenantContext>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenant.Object);
    }
}
