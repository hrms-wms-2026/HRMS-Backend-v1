using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
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

    [Fact]
    public async Task GetListAsync_ActiveOnly_LeavesOutResolvedCases_UnlessAStatusIsAskedFor()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        foreach (var status in Enum.GetValues<ExceptionStatus>())
        {
            db.Exceptions.Add(new DomainException
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = Guid.NewGuid(),
                Type = ExceptionType.IdentityAnomaly, Status = status, Title = "t", Description = "d", DetectedAt = DayStart
            });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var repository = new EfExceptionRepository(db);

        var active = await repository.GetListAsync(
            tenantId, new ExceptionListFilter(null, null, null, null, ActiveOnly: true), 1, 20, CancellationToken.None);
        var resolved = await repository.GetListAsync(
            tenantId, new ExceptionListFilter(ExceptionStatus.Resolved, null, null, null, ActiveOnly: true), 1, 20, CancellationToken.None);

        active.Should().NotBeEmpty().And.OnlyContain(e => e.Status != ExceptionStatus.Resolved);
        active.Should().HaveCount(Enum.GetValues<ExceptionStatus>().Length - 1);
        (await repository.GetListTotalCountAsync(
            tenantId, new ExceptionListFilter(null, null, null, null, ActiveOnly: true), CancellationToken.None))
            .Should().Be(active.Count);
        resolved.Should().ContainSingle(e => e.Status == ExceptionStatus.Resolved);
    }

    [Fact]
    public async Task CountDetectedInRangeAsync_CountsOnlyThisEmployeeInsideTheRange()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        DomainException Case(Guid employee, DateTimeOffset detectedAt, ExceptionStatus status = ExceptionStatus.Open) => new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee,
            Type = ExceptionType.IdentityAnomaly, Status = status, Title = "t", Description = "d",
            DetectedAt = detectedAt
        };
        db.Exceptions.AddRange(
            Case(employeeId, DayStart.AddHours(-1)),
            Case(employeeId, DayStart),
            Case(employeeId, DayStart.AddDays(1).AddHours(-1), ExceptionStatus.Resolved),
            Case(employeeId, DayStart.AddDays(1)),
            Case(Guid.NewGuid(), DayStart.AddHours(3)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var count = await new EfExceptionRepository(db).CountDetectedInRangeAsync(
            tenantId, employeeId, DayStart, DayStart.AddDays(1), CancellationToken.None);

        // The resolved case no longer counts (inbox plan X1 count fix).
        count.Should().Be(1);
    }

    [Fact]
    public async Task CountAndListDetectedInRange_OnlyCountOpenAndAcknowledgedCases()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        foreach (var status in Enum.GetValues<ExceptionStatus>())
            db.Exceptions.Add(new DomainException
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, Type = ExceptionType.IdentityAnomaly,
                Status = status, Title = status.ToString(), Description = "d", DetectedAt = DayStart.AddHours(2)
            });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var repository = new EfExceptionRepository(db);

        var count = await repository.CountDetectedInRangeAsync(tenantId, employeeId, DayStart, DayStart.AddDays(1), CancellationToken.None);
        var listed = await repository.ListDetectedInRangeAsync(tenantId, employeeId, DayStart, DayStart.AddDays(1), 10, CancellationToken.None);

        count.Should().Be(2);
        listed.Select(e => e.Status).Should().BeEquivalentTo([ExceptionStatus.Open, ExceptionStatus.Acknowledged]);
    }

    [Fact]
    public async Task CountActiveInboxAsync_Counts_the_visibility_union_once_and_excludes_resolved_calendar_and_other_tenants()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var caller = Guid.NewGuid();
        var routedEmployee = Guid.NewGuid();
        var heldEmployee = Guid.NewGuid();
        var addedEmployee = Guid.NewGuid();
        var legacyEmployee = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();

        DomainException Case(Guid tenant, Guid employee, ExceptionStatus status = ExceptionStatus.Open,
            ExceptionType type = ExceptionType.SustainedLowActivity) => new()
        {
            Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employee, Type = type, Status = status,
            Title = "t", Description = "d", DetectedAt = DayStart
        };

        var own = Case(tenantId, caller);
        var routed = Case(tenantId, routedEmployee);
        var held = Case(tenantId, heldEmployee);
        var added = Case(tenantId, addedEmployee);
        var legacyHr = Case(tenantId, legacyEmployee, ExceptionStatus.Escalated);
        var journaledEscalation = Case(tenantId, legacyEmployee, ExceptionStatus.Escalated);
        db.Exceptions.AddRange(
            own, routed, held, added, legacyHr, journaledEscalation,
            Case(tenantId, caller, ExceptionStatus.Resolved),
            Case(tenantId, caller, type: ExceptionType.MeetingNoShow),
            Case(otherTenant, caller));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfExceptionRepository(db);
        var count = await repository.CountActiveInboxAsync(tenantId, new ExceptionInboxCountCriteria(
            CallerEmployeeId: caller,
            RoutedEmployeeIds: [routedEmployee],
            HrFallbackEmployeeIds: [legacyEmployee],
            HeldCaseIds: [held.Id],
            AddedCaseIds: [added.Id, routed.Id],
            IncludeLegacyHrEscalations: true,
            JournaledEscalatedCaseIds: [journaledEscalation.Id],
            Calendar: false), CancellationToken.None);

        count.Should().Be(5, "own, routed, held, chat-added and pre-journal HR cases are visible, with overlaps counted once");
        (await repository.ListActiveInboxEmployeeIdsAsync(tenantId, calendar: false, CancellationToken.None))
            .Should().BeEquivalentTo([caller, routedEmployee, heldEmployee, addedEmployee, legacyEmployee]);
    }

    [Fact]
    public async Task CountActiveInboxAsync_HR_fallback_counts_open_cases_of_no_approver_employees_except_journaled_escalations()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var noApprover = Guid.NewGuid();
        DomainException Case(ExceptionStatus status) => new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = noApprover, Type = ExceptionType.SustainedLowActivity, Status = status,
            Title = "t", Description = "d", DetectedAt = DayStart
        };
        var open = Case(ExceptionStatus.Open);
        var acknowledged = Case(ExceptionStatus.Acknowledged);
        var escalatedElsewhere = Case(ExceptionStatus.Escalated);
        db.Exceptions.AddRange(open, acknowledged, escalatedElsewhere);
        await db.SaveChangesAsync();

        var count = await new EfExceptionRepository(db).CountActiveInboxAsync(tenantId, new ExceptionInboxCountCriteria(
            CallerEmployeeId: Guid.NewGuid(),
            RoutedEmployeeIds: [],
            HrFallbackEmployeeIds: [noApprover],
            HeldCaseIds: [],
            AddedCaseIds: [],
            IncludeLegacyHrEscalations: false,
            JournaledEscalatedCaseIds: [escalatedElsewhere.Id],
            Calendar: false), CancellationToken.None);

        count.Should().Be(2, "HR keeps the open and acknowledged cases; the journaled escalation went to someone else");
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
