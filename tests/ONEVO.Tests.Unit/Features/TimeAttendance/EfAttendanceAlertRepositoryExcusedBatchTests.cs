using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Alerts;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class EfAttendanceAlertRepositoryExcusedBatchTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), Outsider = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 10, 1), To = new(2026, 10, 31);

    private readonly ApplicationDbContext _db = BuildInMemoryDb();

    private void Add(Guid employeeId, DateOnly date, string signal, string? outcome, Guid? tenantId = null) =>
        _db.AttendanceAlerts.Add(new AttendanceAlert
        {
            Id = Guid.NewGuid(), TenantId = tenantId ?? TenantId, EmployeeId = employeeId, WorkDate = date,
            SignalKey = signal, Status = AttendanceAlertStatuses.Resolved, Outcome = outcome,
        });

    [Fact]
    public async Task ExcusedDatesForEmployees_GroupsByEmployeeAndSignal_SameFilterAsSingle()
    {
        Add(A, new DateOnly(2026, 10, 5), AttendanceAlertSignals.LateClockIns, AttendanceAlertOutcomes.Excused);
        Add(A, new DateOnly(2026, 10, 6), AttendanceAlertSignals.AbsentDays, AttendanceAlertOutcomes.ConvertedToLeave);
        Add(A, new DateOnly(2026, 10, 7), AttendanceAlertSignals.AbsentDays, AttendanceAlertOutcomes.Confirmed);
        Add(B, new DateOnly(2026, 10, 8), AttendanceAlertSignals.LateClockIns, AttendanceAlertOutcomes.Excused);
        Add(B, new DateOnly(2026, 11, 2), AttendanceAlertSignals.LateClockIns, AttendanceAlertOutcomes.Excused);
        Add(Outsider, new DateOnly(2026, 10, 9), AttendanceAlertSignals.LateClockIns, AttendanceAlertOutcomes.Excused);
        Add(A, new DateOnly(2026, 10, 10), AttendanceAlertSignals.LateClockIns, AttendanceAlertOutcomes.Excused, Guid.NewGuid());
        await _db.SaveChangesAsync();
        var repo = new EfAttendanceAlertRepository(_db);

        var batch = await repo.ExcusedDatesForEmployeesAsync(TenantId, [A, B], From, To, CancellationToken.None);

        batch.Keys.Should().BeEquivalentTo([A, B]);
        batch[A][AttendanceAlertSignals.LateClockIns].Should().BeEquivalentTo([new DateOnly(2026, 10, 5)]);
        batch[A][AttendanceAlertSignals.AbsentDays].Should().BeEquivalentTo([new DateOnly(2026, 10, 6)]);
        batch[B][AttendanceAlertSignals.LateClockIns].Should().BeEquivalentTo([new DateOnly(2026, 10, 8)]);
        foreach (var id in new[] { A, B })
        {
            var single = await repo.ExcusedDatesAsync(TenantId, id, From, To, CancellationToken.None);
            batch[id].Keys.Should().BeEquivalentTo(single.Keys);
        }
    }

    [Fact]
    public async Task ExcusedDatesForEmployees_EmptyIds_ReturnsEmpty()
    {
        var batch = await new EfAttendanceAlertRepository(_db).ExcusedDatesForEmployeesAsync(TenantId, [], From, To, CancellationToken.None);
        batch.Should().BeEmpty();
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var clock = new Mock<IDateTimeProvider>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(new Mock<MediatR.IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }
}
