using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Alerts;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using ONEVO.Domain.Features.TimeAttendance.Alerts;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class AttendanceAlertMaterializerTests
{
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(5.5), "t", "t");
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateOnly Aug3 = new(2026, 8, 3), Aug4 = new(2026, 8, 4), Aug5 = new(2026, 8, 5), Aug6 = new(2026, 8, 6), Aug7 = new(2026, 8, 7);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Mock<IEmployeeAttendancePeriodReader> _reader = new();
    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly ApplicationDbContext _db = BuildInMemoryDb();
    private readonly List<Notification> _alertNotifications = [];

    public AttendanceAlertMaterializerTests()
    {
        _clock.SetupGet(c => c.Today).Returns(Today);
        _clock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.Parse("2026-08-21T04:00:00+00:00"));
        _notifications.Setup(n => n.ListByTypesAsync(_tenantId, _employeeId, It.IsAny<IReadOnlyCollection<NotificationType>>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, IReadOnlyCollection<NotificationType> types, DateTimeOffset _, DateTimeOffset _, int _, CancellationToken _) =>
                _alertNotifications.Where(n => types.Contains(n.Type)).ToList());
    }

    private AttendanceAlertMaterializer Materializer() =>
        new(_reader.Object, new EfAttendanceAlertRepository(_db), _notifications.Object, _clock.Object);

    private AttendanceRecord Rec(DateOnly d, string start, string end) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, Date = d, ExpectedWorkingDay = true,
        ScheduledStart = new TimeOnly(9, 0), ScheduledEnd = new TimeOnly(17, 30),
        ActualStart = DateTimeOffset.Parse(start), ActualEnd = DateTimeOffset.Parse(end)
    };

    private AttendanceRecord OnTime(DateOnly d) => Rec(d, $"{d:yyyy-MM-dd}T03:30:00+00:00", $"{d:yyyy-MM-dd}T12:00:00+00:00");

    // Aug 3-7 are the only expected days: Aug 3 on time, Aug 4 late, Aug 5-7 absent unless a record is given.
    private void ArrangeDays(params AttendanceRecord[] extra)
    {
        var records = new List<AttendanceRecord> { OnTime(Aug3), Rec(Aug4, "2026-08-04T04:15:00+00:00", "2026-08-04T12:00:00+00:00") };
        records.AddRange(extra);
        _reader.Setup(r => r.LoadAsync(_tenantId, _employeeId, _legalEntityId, It.IsAny<EmployeePeriod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendancePeriodData(
                records, Colombo, DateTimeOffset.Parse("2026-08-21T04:00:00+00:00"), Today, null, new Dictionary<DateOnly, int>(),
                Array.Empty<LeaveRequest>(), DateTimeOffset.Parse("2026-07-16T18:30:00+00:00"), DateTimeOffset.Parse("2026-08-20T18:30:00+00:00"),
                ExpectedWorkdayCalendar.Build(new HashSet<int> { 1, 2, 3, 4, 5 }, new HashSet<DateOnly>(), Aug3, Aug7, Aug3, null, Today),
                // Already-excused days still materialise: the alert itself is what carries the resolution.
                new Dictionary<string, IReadOnlySet<DateOnly>> { [AttendanceAlertSignals.AbsentDays] = new HashSet<DateOnly> { Aug7 } }));
    }

    private Notification LocationAlert(string createdAtUtc) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, Type = NotificationType.OutsideWorkLocationAlert,
        Title = "Outside work location", Message = "m", CreatedAt = DateTimeOffset.Parse(createdAtUtc)
    };

    private Task<List<AttendanceAlert>> Alerts() => _db.AttendanceAlerts.AsNoTracking().OrderBy(a => a.WorkDate).ThenBy(a => a.SignalKey).ToListAsync();

    [Fact]
    public async Task Materialize_CreatesOneOpenAlertPerQualifyingDayAndSignal_AndIsIdempotent()
    {
        ArrangeDays();
        var location = LocationAlert("2026-08-05T20:00:00+00:00"); // Aug 6 in Colombo
        _alertNotifications.Add(location);

        var first = await Materializer().MaterializeAsync(_tenantId, _employeeId, _legalEntityId, CancellationToken.None);
        var second = await Materializer().MaterializeAsync(_tenantId, _employeeId, _legalEntityId, CancellationToken.None);

        first.Added.Should().Be(5);
        second.Should().Be(new AttendanceAlertMaterializeResult(0, 0));
        var alerts = await Alerts();
        alerts.Select(a => (a.WorkDate, a.SignalKey)).Should().BeEquivalentTo(new[]
        {
            (Aug4, AttendanceAlertSignals.LateClockIns),
            (Aug5, AttendanceAlertSignals.AbsentDays),
            (Aug6, AttendanceAlertSignals.AbsentDays),
            (Aug6, AttendanceAlertSignals.LocationViolations),
            (Aug7, AttendanceAlertSignals.AbsentDays),
        });
        alerts.Should().OnlyContain(a => a.Status == AttendanceAlertStatuses.Open && a.TenantId == _tenantId && a.LegalEntityId == _legalEntityId);
        alerts.Single(a => a.SignalKey == AttendanceAlertSignals.LocationViolations).SourceId.Should().Be(location.Id);
        alerts.Where(a => !AttendanceAlertSignals.NotificationBacked.Contains(a.SignalKey)).Should().OnlyContain(a => a.SourceId == null);
    }

    [Fact]
    public async Task Materialize_RemovesAnOpenAlertWhoseDayNoLongerQualifies_ButKeepsResolvedOnes()
    {
        ArrangeDays();
        await Materializer().MaterializeAsync(_tenantId, _employeeId, _legalEntityId, CancellationToken.None);
        var aug5 = await _db.AttendanceAlerts.SingleAsync(a => a.WorkDate == Aug5);
        aug5.Status = AttendanceAlertStatuses.Resolved;
        aug5.Outcome = AttendanceAlertOutcomes.Confirmed;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Aug 5 and Aug 6 turn out to have been worked (e.g. a correction was approved later).
        ArrangeDays(OnTime(Aug5), OnTime(Aug6));
        var result = await Materializer().MaterializeAsync(_tenantId, _employeeId, _legalEntityId, CancellationToken.None);

        result.Should().Be(new AttendanceAlertMaterializeResult(0, 1));
        (await Alerts()).Select(a => (a.WorkDate, a.SignalKey, a.Status)).Should().BeEquivalentTo(new[]
        {
            (Aug4, AttendanceAlertSignals.LateClockIns, AttendanceAlertStatuses.Open),
            (Aug5, AttendanceAlertSignals.AbsentDays, AttendanceAlertStatuses.Resolved),
            (Aug7, AttendanceAlertSignals.AbsentDays, AttendanceAlertStatuses.Open),
        });
    }

    [Fact]
    public async Task Materialize_CanRecreateADayThatQualifiesAgainAfterItsOpenAlertWasRemoved()
    {
        ArrangeDays();
        await Materializer().MaterializeAsync(_tenantId, _employeeId, _legalEntityId, CancellationToken.None);
        ArrangeDays(OnTime(Aug5));
        await Materializer().MaterializeAsync(_tenantId, _employeeId, _legalEntityId, CancellationToken.None);
        _db.ChangeTracker.Clear();
        ArrangeDays();

        var result = await Materializer().MaterializeAsync(_tenantId, _employeeId, _legalEntityId, CancellationToken.None);

        result.Should().Be(new AttendanceAlertMaterializeResult(1, 0));
        (await Alerts()).Should().Contain(a => a.WorkDate == Aug5 && a.SignalKey == AttendanceAlertSignals.AbsentDays);
    }

    [Fact]
    public async Task Materialize_ReadsTheWindowFrom35DaysAgoToYesterday()
    {
        ArrangeDays();

        await Materializer().MaterializeAsync(_tenantId, _employeeId, _legalEntityId, CancellationToken.None);

        _reader.Verify(r => r.LoadAsync(_tenantId, _employeeId, _legalEntityId,
            new EmployeePeriod(Today.AddDays(-AttendanceAlertMaterializer.WindowDays), Today.AddDays(-1)), It.IsAny<CancellationToken>()));
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
