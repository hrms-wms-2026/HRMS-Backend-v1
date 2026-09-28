using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Exceptions;
using Xunit;
using DomainException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Tests.Unit.Features.Monitoring.Exceptions;

public sealed class EfExceptionRepositoryTests
{
    private static readonly DateTimeOffset DayStart = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    // Same-day repeat while the first case is open: suppressed.
    [InlineData(2, ExceptionStatus.Open, true)]
    // Acknowledged but not resolved, still the same day: suppressed.
    [InlineData(2, ExceptionStatus.Acknowledged, true)]
    // Resolved the same day: a new mismatch opens a new case.
    [InlineData(2, ExceptionStatus.Resolved, false)]
    // Left acknowledged from yesterday: a mismatch today opens a new case.
    [InlineData(-10, ExceptionStatus.Acknowledged, false)]
    public async Task HasUnresolvedSinceAsync_OnlyCountsOpenCasesFromTheSameDay(int hoursFromDayStart, ExceptionStatus status, bool expected)
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        db.Exceptions.Add(new DomainException
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
            Type = ExceptionType.IdentityAnomaly, Status = status, Title = "t", Description = "d",
            DetectedAt = DayStart.AddHours(hoursFromDayStart)
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new EfExceptionRepository(db).HasUnresolvedSinceAsync(
            tenantId, employeeId, ExceptionType.IdentityAnomaly, DayStart, CancellationToken.None);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task HasUnresolvedSinceAsync_IgnoresOtherEmployeesAndTypes()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        db.Exceptions.AddRange(
            new DomainException
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = Guid.NewGuid(),
                Type = ExceptionType.IdentityAnomaly, Status = ExceptionStatus.Open, Title = "t", Description = "d",
                DetectedAt = DayStart.AddHours(1)
            },
            new DomainException
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
                Type = ExceptionType.SustainedLowActivity, Status = ExceptionStatus.Open, Title = "t", Description = "d",
                DetectedAt = DayStart.AddHours(1)
            });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new EfExceptionRepository(db).HasUnresolvedSinceAsync(
            tenantId, employeeId, ExceptionType.IdentityAnomaly, DayStart, CancellationToken.None);

        result.Should().BeFalse();
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dateTimeProvider = new Mock<IDateTimeProvider>();

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(new Mock<IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }
}
