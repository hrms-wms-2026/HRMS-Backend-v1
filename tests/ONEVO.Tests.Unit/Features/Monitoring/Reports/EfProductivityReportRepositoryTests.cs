using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Reports.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.WorkSessions.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Reports;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Reports;

public class EfProductivityReportRepositoryTests
{
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

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    // Deliberately distinct from EmployeeId: work sessions are keyed by the tray device's
    // JWT UserId, while reports are requested by CoreHR Employee.Id.
    private static readonly Guid UserId = Guid.NewGuid();

    private static Employee CreateEmployee(Guid tenantId, Guid employeeId, Guid userId, Guid? departmentId = null) =>
        new() { Id = employeeId, TenantId = tenantId, UserId = userId, DepartmentId = departmentId };

    private static EmployeeWorkSession CreateWorkSession(Guid tenantId, Guid userId, int workSeconds, int breakSeconds) =>
        new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, DeviceRegistrationId = Guid.NewGuid(),
            ClockInAt = new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero),
            ClockOutAt = new DateTimeOffset(2026, 8, 10, 17, 0, 0, TimeSpan.Zero),
            AccumulatedWorkSeconds = workSeconds, AccumulatedBreakSeconds = breakSeconds, BreakSessionCount = 1
        };

    [Fact]
    public async Task GetEmployeeAggregateAsync_SumsDailySummariesAndWorkSessionsInRange()
    {
        await using var db = BuildDb();
        db.ActivityDailySummaries.AddRange(
            new ActivityDailySummary { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, Date = new DateOnly(2026, 8, 10), TotalActiveMinutes = 200, TotalIdleMinutes = 40, TotalMeetingMinutes = 20, ProductiveAppMinutes = 100, PersonalAppMinutes = 10, UnknownAppMinutes = 90, ActivityScore = 60m },
            new ActivityDailySummary { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, Date = new DateOnly(2026, 8, 11), TotalActiveMinutes = 220, TotalIdleMinutes = 30, TotalMeetingMinutes = 0, ProductiveAppMinutes = 150, PersonalAppMinutes = 0, UnknownAppMinutes = 70, ActivityScore = 70m },
            new ActivityDailySummary { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, Date = new DateOnly(2026, 8, 20), TotalActiveMinutes = 999, TotalIdleMinutes = 0, TotalMeetingMinutes = 0, ProductiveAppMinutes = 0, PersonalAppMinutes = 0, UnknownAppMinutes = 0, ActivityScore = 0m });
        db.Employees.Add(CreateEmployee(TenantId, EmployeeId, UserId));
        db.EmployeeWorkSessions.Add(CreateWorkSession(TenantId, UserId, 7 * 3600, 1800));
        await db.SaveChangesAsync();

        var repo = new EfProductivityReportRepository(db);
        var result = await repo.GetEmployeeAggregateAsync(
            TenantId, EmployeeId, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 11), CancellationToken.None);

        result.TotalActiveMinutes.Should().Be(420);
        result.TotalMeetingMinutes.Should().Be(20);
        result.ProductiveAppMinutes.Should().Be(250);
        result.DayCount.Should().Be(2);
        result.AverageActivityScore.Should().Be(65m);
        result.TotalWorkedMinutes.Should().Be(420);
        result.TotalBreakMinutes.Should().Be(30);
    }

    [Fact]
    public async Task GetEmployeeAggregateAsync_NoDataInRange_ReturnsAllZero()
    {
        await using var db = BuildDb();
        var repo = new EfProductivityReportRepository(db);

        var result = await repo.GetEmployeeAggregateAsync(
            TenantId, EmployeeId, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 11), CancellationToken.None);

        result.DayCount.Should().Be(0);
        result.TotalActiveMinutes.Should().Be(0);
    }

    [Fact]
    public async Task GetDepartmentAggregateAsync_UnknownDepartment_ReturnsNull()
    {
        await using var db = BuildDb();
        var repo = new EfProductivityReportRepository(db);

        var result = await repo.GetDepartmentAggregateAsync(
            TenantId, Guid.NewGuid(), new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 11), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetEmployeeAggregateAsync_CountsWorkSessionsKeyedByTheEmployeesUserId()
    {
        // Regression: work sessions store the device's UserId, not Employee.Id. Filtering
        // sessions by employee id directly returned 0 worked minutes, which silently disabled
        // ExceptionDetectionJob's AttendanceIrregularity rule.
        await using var db = BuildDb();
        db.Employees.Add(CreateEmployee(TenantId, EmployeeId, UserId));
        db.EmployeeWorkSessions.AddRange(
            CreateWorkSession(TenantId, UserId, 4 * 3600, 600),
            CreateWorkSession(TenantId, UserId, 3 * 3600, 1200),
            // A session stored under the employee id must not match - it is not a user id.
            CreateWorkSession(TenantId, EmployeeId, 9 * 3600, 9 * 60));
        await db.SaveChangesAsync();

        var repo = new EfProductivityReportRepository(db);
        var result = await repo.GetEmployeeAggregateAsync(
            TenantId, EmployeeId, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 10), CancellationToken.None);

        result.TotalWorkedMinutes.Should().Be(420);
        result.TotalBreakMinutes.Should().Be(30);
    }

    [Fact]
    public async Task GetEmployeeAggregateAsync_IgnoresSameUsersSessionsInAnotherTenant()
    {
        await using var db = BuildDb();
        var otherTenantId = Guid.NewGuid();
        db.Employees.AddRange(
            CreateEmployee(TenantId, EmployeeId, UserId),
            CreateEmployee(otherTenantId, Guid.NewGuid(), UserId));
        db.EmployeeWorkSessions.AddRange(
            CreateWorkSession(TenantId, UserId, 2 * 3600, 0),
            CreateWorkSession(otherTenantId, UserId, 5 * 3600, 0));
        await db.SaveChangesAsync();

        var repo = new EfProductivityReportRepository(db);
        var result = await repo.GetEmployeeAggregateAsync(
            TenantId, EmployeeId, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 10), CancellationToken.None);

        result.TotalWorkedMinutes.Should().Be(120);
    }

    [Fact]
    public async Task GetDepartmentAggregateAsync_CountsWorkSessionsOfDepartmentMembersByUserId()
    {
        await using var db = BuildDb();
        var departmentId = Guid.NewGuid();
        var memberUserId = Guid.NewGuid();
        var outsiderUserId = Guid.NewGuid();
        db.Departments.Add(new ONEVO.Domain.Features.OrgStructure.Entities.Department { Id = departmentId, TenantId = TenantId, Name = "Eng" });
        db.Employees.AddRange(
            CreateEmployee(TenantId, Guid.NewGuid(), memberUserId, departmentId),
            CreateEmployee(TenantId, Guid.NewGuid(), outsiderUserId));
        db.EmployeeWorkSessions.AddRange(
            CreateWorkSession(TenantId, memberUserId, 6 * 3600, 900),
            CreateWorkSession(TenantId, outsiderUserId, 8 * 3600, 0));
        await db.SaveChangesAsync();

        var repo = new EfProductivityReportRepository(db);
        var result = await repo.GetDepartmentAggregateAsync(
            TenantId, departmentId, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 10), CancellationToken.None);

        result.Should().NotBeNull();
        result!.TotalWorkedMinutes.Should().Be(360);
        result.TotalBreakMinutes.Should().Be(15);
    }
}
