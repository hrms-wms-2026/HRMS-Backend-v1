using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar.Exceptions;

/// <summary>Plan BZ coverage audit: the incident store's case evidence list and the nightly purge.</summary>
public sealed class EfCalendarIncidentRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);
    private readonly string _database = Guid.NewGuid().ToString();
    private readonly Guid _employee = Guid.NewGuid();

    private ApplicationDbContext Db()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_database).Options;
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);
        return new ApplicationDbContext(options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(new Mock<MediatR.IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }

    private CalendarMeetingIncident Incident(DateTimeOffset start, Guid? exceptionId = null, Guid? tenantId = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId ?? TenantId, EmployeeId = _employee, RuleCode = "no_show", EventId = Guid.NewGuid(),
        OccurrenceStart = start, DetectedAt = start, ExceptionId = exceptionId,
    };

    [Fact]
    public async Task The_purge_removes_only_this_tenants_unattached_incidents_older_than_the_cutoff()
    {
        var caseId = Guid.NewGuid();
        CalendarMeetingIncident oldLoose = Incident(Now.AddDays(-100)), oldAttached = Incident(Now.AddDays(-100), caseId),
            recentLoose = Incident(Now.AddDays(-5)), otherTenant = Incident(Now.AddDays(-100), tenantId: Guid.NewGuid());
        await using (var seed = Db())
        {
            seed.CalendarMeetingIncidents.AddRange(oldLoose, oldAttached, recentLoose, otherTenant);
            await seed.SaveChangesAsync();
        }

        await using (var db = Db())
        {
            var repo = new EfCalendarIncidentRepository(db);
            (await repo.PurgeUnattachedAsync(TenantId, Now.AddDays(-90), default)).Should().Be(1);
            await repo.SaveChangesAsync(default);
        }

        await using var check = Db();
        (await check.CalendarMeetingIncidents.IgnoreQueryFilters().Where(i => i.Id == oldLoose.Id && !i.IsDeleted).AnyAsync())
            .Should().BeFalse("the old unattached incident is gone");
        (await check.CalendarMeetingIncidents.IgnoreQueryFilters().Where(i => !i.IsDeleted).Select(i => i.Id).ToListAsync())
            .Should().BeEquivalentTo([oldAttached.Id, recentLoose.Id, otherTenant.Id]);
    }

    [Fact]
    public async Task A_cases_evidence_is_its_own_incidents_in_occurrence_order()
    {
        var caseId = Guid.NewGuid();
        CalendarMeetingIncident later = Incident(Now.AddDays(-1), caseId), earlier = Incident(Now.AddDays(-3), caseId);
        await using (var seed = Db())
        {
            seed.CalendarMeetingIncidents.AddRange(later, earlier, Incident(Now.AddDays(-2), Guid.NewGuid()), Incident(Now.AddDays(-2)));
            await seed.SaveChangesAsync();
        }

        await using var db = Db();
        var list = await new EfCalendarIncidentRepository(db).ListForExceptionAsync(TenantId, caseId, default);

        list.Select(i => i.Id).Should().Equal(earlier.Id, later.Id);
        (await new EfCalendarIncidentRepository(db).ListForExceptionAsync(Guid.NewGuid(), caseId, default)).Should().BeEmpty("another tenant");
    }
}
