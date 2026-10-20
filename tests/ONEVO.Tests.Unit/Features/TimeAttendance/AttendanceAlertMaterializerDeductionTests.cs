using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Alerts;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using ONEVO.Domain.Features.TimeAttendance.Alerts;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;
using ONEVO.Tests.Unit.Fakes;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

/// <summary>The daily job charges, re-charges and refunds late/early deductions under the clock-in policy.</summary>
public sealed class AttendanceAlertMaterializerDeductionTests
{
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(5.5), "t", "t");
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateOnly Aug3 = new(2026, 8, 3), Aug4 = new(2026, 8, 4), Aug5 = new(2026, 8, 5);
    private static readonly DateTimeOffset UtcNow = DateTimeOffset.Parse("2026-08-21T04:00:00+00:00");
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Guid _annualLeave = Guid.NewGuid();
    private readonly Mock<IEmployeeAttendancePeriodReader> _reader = new();
    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<IEmployeeClockInPolicyResolver> _policies = new();
    private readonly Mock<ILeaveEntitlementRepository> _entitlementRepo = new();
    private readonly Mock<ILeaveApprovalRepository> _auditRepo = new();
    private readonly List<LeaveBalanceAudit> _audits = [];
    private readonly LeaveEntitlement _entitlement;
    private readonly ApplicationDbContext _db = BuildInMemoryDb();
    private ClockInPolicy? _policy;

    public AttendanceAlertMaterializerDeductionTests()
    {
        _entitlement = new LeaveEntitlement { Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, LeaveTypeId = _annualLeave, Year = 2026, TotalHours = 80m };
        _notifications.Setup(n => n.ListByTypesAsync(_tenantId, _employeeId, It.IsAny<IReadOnlyCollection<NotificationType>>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Notification>());
        _policies.Setup(p => p.ResolveAsync(_tenantId, _employeeId, _legalEntityId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _policy);
        _entitlementRepo.Setup(e => e.GetTrackedByEmployeeTypeYearAsync(_tenantId, _employeeId, _annualLeave, 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_entitlement);
        _auditRepo.Setup(a => a.AddBalanceAuditAsync(It.IsAny<LeaveBalanceAudit>(), It.IsAny<CancellationToken>()))
            .Callback((LeaveBalanceAudit a, CancellationToken _) => _audits.Add(a)).Returns(Task.CompletedTask);
    }

    private AttendanceAlertMaterializer Materializer()
    {
        var clock = new FakeDateTimeProvider { UtcNow = UtcNow };
        var deductions = new AttendanceDeductionService(new EfAttendanceAlertDeductionRepository(_db), _entitlementRepo.Object, _auditRepo.Object, clock);
        return new(_reader.Object, new EfAttendanceAlertRepository(_db), _notifications.Object, clock, _policies.Object, deductions,
            new EfAttendanceAlertDeductionRepository(_db));
    }

    private ClockInPolicy Policy(int minutes = 15, decimal multiplier = 1m, DateTimeOffset? rulesChangedAt = null)
    {
        var policy = new ClockInPolicy { Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, IsActive = true,
            DeductionRulesChangedAt = rulesChangedAt ?? DateTimeOffset.Parse("2026-08-01T00:00:00+00:00") };
        policy.LateDeductionRules.Add(new ClockInLateDeductionRule { Id = Guid.NewGuid(), LateArrivalMinute = minutes, Multiplier = multiplier, TimeOffTypeId = _annualLeave, IsActive = true });
        return policy;
    }

    private AttendanceRecord Rec(DateOnly d, string start, string end) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, Date = d, ExpectedWorkingDay = true,
        ScheduledStart = new TimeOnly(9, 0), ScheduledEnd = new TimeOnly(17, 30),
        ActualStart = DateTimeOffset.Parse(start), ActualEnd = DateTimeOffset.Parse(end)
    };

    // 09:00-17:30 Colombo is 03:30-12:00 UTC.
    private AttendanceRecord OnTime(DateOnly d) => Rec(d, $"{d:yyyy-MM-dd}T03:30:00+00:00", $"{d:yyyy-MM-dd}T12:00:00+00:00");
    private AttendanceRecord Late45(DateOnly d) => Rec(d, $"{d:yyyy-MM-dd}T04:15:00+00:00", $"{d:yyyy-MM-dd}T12:00:00+00:00");
    private AttendanceRecord LeftHourEarly(DateOnly d) => Rec(d, $"{d:yyyy-MM-dd}T03:30:00+00:00", $"{d:yyyy-MM-dd}T11:00:00+00:00");

    /// <summary>Aug 3-5 expected; every day worked (no absences) unless records say otherwise.</summary>
    private void ArrangeDays(int threshold, params AttendanceRecord[] records)
    {
        var byDate = records.ToDictionary(r => r.Date);
        foreach (var d in new[] { Aug3, Aug4, Aug5 })
            byDate.TryAdd(d, OnTime(d));
        _reader.Setup(r => r.LoadAsync(_tenantId, _employeeId, _legalEntityId, It.IsAny<EmployeePeriod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendancePeriodData(
                byDate.Values.ToList(), Colombo, UtcNow, Today, null, new Dictionary<DateOnly, int>(),
                Array.Empty<LeaveRequest>(), DateTimeOffset.Parse("2026-07-16T18:30:00+00:00"), DateTimeOffset.Parse("2026-08-20T18:30:00+00:00"),
                ExpectedWorkdayCalendar.Build(new HashSet<int> { 1, 2, 3, 4, 5 }, new HashSet<DateOnly>(), Aug3, Aug5, Aug3, null, Today),
                null, LateEarlyThresholdMinutes: threshold));
    }

    private Task Run() => Materializer().MaterializeAsync(_tenantId, _employeeId, _legalEntityId, CancellationToken.None);

    private async Task<AttendanceAlertDeduction?> DeductionFor(DateOnly day, string signal)
    {
        _db.ChangeTracker.Clear();
        var alert = await _db.AttendanceAlerts.IgnoreQueryFilters().SingleAsync(a => a.WorkDate == day && a.SignalKey == signal);
        return await _db.AttendanceAlertDeductions.AsNoTracking().SingleOrDefaultAsync(d => d.AlertId == alert.Id);
    }

    [Fact]
    public async Task LateDay_PastFirstRow_RaisesAlertAndCharges()
    {
        _policy = Policy();
        ArrangeDays(15, Late45(Aug4));

        await Run();

        var deduction = await DeductionFor(Aug4, AttendanceAlertSignals.LateClockIns);
        deduction!.Status.Should().Be(AttendanceAlertDeductionStatuses.Charged);
        deduction.Hours.Should().Be(0.75m);
        _entitlement.UsedHours.Should().Be(0.75m);
        _audits.Should().ContainSingle();
    }

    [Fact]
    public async Task RunTwice_ChargesOnce()
    {
        _policy = Policy();
        ArrangeDays(15, Late45(Aug4));

        await Run();
        _db.ChangeTracker.Clear();
        await Run();

        _audits.Should().ContainSingle();
        _entitlement.UsedHours.Should().Be(0.75m);
    }

    [Fact]
    public async Task LateDay_BelowFirstRow_NoAlertNoCharge()
    {
        _policy = Policy(minutes: 60);
        ArrangeDays(60, Late45(Aug4));

        await Run();

        (await _db.AttendanceAlerts.AnyAsync(a => a.SignalKey == AttendanceAlertSignals.LateClockIns)).Should().BeFalse();
        _audits.Should().BeEmpty();
    }

    [Fact]
    public async Task EarlyClockOut_ChargedWithSameRows()
    {
        _policy = Policy(multiplier: 1.5m);
        ArrangeDays(15, LeftHourEarly(Aug5));

        await Run();

        (await DeductionFor(Aug5, AttendanceAlertSignals.EarlyClockOuts))!.Hours.Should().Be(1.5m);
        _audits.Single().Reason.Should().StartWith("Early clock-out 05 Aug · 60 min × 1.5");
    }

    [Fact]
    public async Task CorrectionErasesLateness_OnConfirmedAlert_Refunds_AlertKept()
    {
        _policy = Policy();
        ArrangeDays(15, Late45(Aug4));
        await Run();
        var alert = await _db.AttendanceAlerts.SingleAsync(a => a.SignalKey == AttendanceAlertSignals.LateClockIns);
        alert.Status = AttendanceAlertStatuses.Resolved;
        alert.Outcome = AttendanceAlertOutcomes.Confirmed;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        ArrangeDays(15); // a correction made Aug 4 on time
        await Run();

        (await DeductionFor(Aug4, AttendanceAlertSignals.LateClockIns))!.Status.Should().Be(AttendanceAlertDeductionStatuses.Refunded);
        _entitlement.UsedHours.Should().Be(0m);
        (await _db.AttendanceAlerts.AnyAsync(a => a.Id == alert.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task OpenAlertThatStopsQualifying_IsRemovedAndRefunded()
    {
        _policy = Policy();
        ArrangeDays(15, Late45(Aug4));
        await Run();
        _db.ChangeTracker.Clear();

        ArrangeDays(15);
        await Run();

        (await DeductionFor(Aug4, AttendanceAlertSignals.LateClockIns))!.Status.Should().Be(AttendanceAlertDeductionStatuses.Refunded);
        _entitlement.UsedHours.Should().Be(0m);
    }

    [Fact]
    public async Task ExcusedAlert_IsNotCharged()
    {
        _policy = Policy();
        ArrangeDays(15, Late45(Aug4));
        await Run();
        var alert = await _db.AttendanceAlerts.SingleAsync(a => a.SignalKey == AttendanceAlertSignals.LateClockIns);
        alert.Status = AttendanceAlertStatuses.Resolved;
        alert.Outcome = AttendanceAlertOutcomes.Excused;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await Run();

        (await DeductionFor(Aug4, AttendanceAlertSignals.LateClockIns))!.Status.Should().Be(AttendanceAlertDeductionStatuses.Refunded);
        _entitlement.UsedHours.Should().Be(0m);
    }

    [Fact]
    public async Task DayBeforeRulesChangedAt_NotCharged()
    {
        _policy = Policy(rulesChangedAt: DateTimeOffset.Parse("2026-08-10T00:00:00+00:00"));
        ArrangeDays(15, Late45(Aug4));

        await Run();

        (await DeductionFor(Aug4, AttendanceAlertSignals.LateClockIns)).Should().BeNull();
        _audits.Should().BeEmpty();
    }

    [Fact]
    public async Task PolicyWithoutAChangeStamp_DoesNotCharge()
    {
        _policy = Policy();
        _policy.DeductionRulesChangedAt = null;
        ArrangeDays(15, Late45(Aug4));

        await Run();

        _audits.Should().BeEmpty();
    }

    [Fact]
    public async Task NoPolicy_KeepsTodaysAlert_NoCharge()
    {
        ArrangeDays(0, Late45(Aug4));

        await Run();

        (await _db.AttendanceAlerts.AnyAsync(a => a.SignalKey == AttendanceAlertSignals.LateClockIns)).Should().BeTrue();
        _audits.Should().BeEmpty();
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
