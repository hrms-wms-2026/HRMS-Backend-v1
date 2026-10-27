using FluentAssertions;
using Moq;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Alerts;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.TimeAttendance.Alerts;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public class AttendanceDeductionServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid AnnualLeave = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 19);

    private readonly Dictionary<Guid, AttendanceAlertDeduction> _deductions = new();
    private readonly Mock<IAttendanceAlertDeductionRepository> _repo = new();
    private readonly Mock<ILeaveEntitlementRepository> _entitlements = new();
    private readonly Mock<ILeaveApprovalRepository> _audits = new();
    private readonly List<LeaveBalanceAudit> _written = [];
    private LeaveEntitlement? _entitlement = new() { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, LeaveTypeId = AnnualLeave, Year = 2026, TotalHours = 1m };

    public AttendanceDeductionServiceTests()
    {
        _repo.Setup(r => r.GetByAlertAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid alertId, CancellationToken _) => _deductions.GetValueOrDefault(alertId));
        _repo.Setup(r => r.Add(It.IsAny<AttendanceAlertDeduction>())).Callback((AttendanceAlertDeduction d) => _deductions[d.AlertId] = d);
        _entitlements.Setup(e => e.GetTrackedByEmployeeTypeYearAsync(TenantId, EmployeeId, AnnualLeave, 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _entitlement);
        _audits.Setup(a => a.AddBalanceAuditAsync(It.IsAny<LeaveBalanceAudit>(), It.IsAny<CancellationToken>()))
            .Callback((LeaveBalanceAudit a, CancellationToken _) => _written.Add(a)).Returns(Task.CompletedTask);
    }

    private AttendanceDeductionService Service() => new(_repo.Object, _entitlements.Object, _audits.Object,
        new FakeDateTimeProvider { UtcNow = new DateTimeOffset(2026, 10, 20, 2, 0, 0, TimeSpan.Zero) });

    private static AttendanceAlert Alert(string signal = AttendanceAlertSignals.LateClockIns) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, WorkDate = Day, SignalKey = signal,
    };

    private static DeductionTarget Target(int minutes, decimal multiplier = 1m) =>
        new(minutes, new ClockInLateDeductionRule { LateArrivalMinute = 15, Multiplier = multiplier, TimeOffTypeId = AnnualLeave, IsActive = true });

    [Fact]
    public async Task Charge_DeductsMinutesTimesMultiplier_AndAudits()
    {
        var alert = Alert();

        await Service().ReconcileAsync(TenantId, alert, Target(30), null, default);

        _entitlement!.UsedHours.Should().Be(0.5m);
        var audit = _written.Should().ContainSingle().Subject;
        (audit.ChangeType, audit.HoursChanged, audit.BalanceAfter).Should().Be((LeaveBalanceChangeTypes.Deduction, -0.5m, 0.5m));
        audit.Reason.Should().Be("Late clock-in 19 Oct · 30 min × 1");
        _deductions[alert.Id].Status.Should().Be(AttendanceAlertDeductionStatuses.Charged);
        _deductions[alert.Id].Hours.Should().Be(0.5m);
    }

    [Fact]
    public async Task Charge_GoesNegative()
    {
        await Service().ReconcileAsync(TenantId, Alert(), Target(90), null, default);

        _written.Single().BalanceAfter.Should().Be(-0.5m);
    }

    [Fact]
    public async Task EarlyClockOut_IsLabelledAsSuch()
    {
        await Service().ReconcileAsync(TenantId, Alert(AttendanceAlertSignals.EarlyClockOuts), Target(20, 1.5m), null, default);

        _written.Single().Reason.Should().Be("Early clock-out 19 Oct · 20 min × 1.5");
        _entitlement!.UsedHours.Should().Be(0.5m);
    }

    [Fact]
    public async Task NoEntitlement_StaysPending_ThenChargesWhenItExists()
    {
        var alert = Alert();
        var saved = _entitlement;
        _entitlement = null;

        await Service().ReconcileAsync(TenantId, alert, Target(30), null, default);
        _deductions[alert.Id].Status.Should().Be(AttendanceAlertDeductionStatuses.Pending);
        _written.Should().BeEmpty();

        _entitlement = saved;
        await Service().ReconcileAsync(TenantId, alert, Target(30), null, default);
        _deductions[alert.Id].Status.Should().Be(AttendanceAlertDeductionStatuses.Charged);
        _written.Should().ContainSingle();
    }

    [Fact]
    public async Task SameMinutes_IsIdempotent()
    {
        var alert = Alert();

        await Service().ReconcileAsync(TenantId, alert, Target(30), null, default);
        await Service().ReconcileAsync(TenantId, alert, Target(30), null, default);

        _written.Should().ContainSingle();
        _entitlement!.UsedHours.Should().Be(0.5m);
    }

    [Fact]
    public async Task MinutesChanged_RefundsAndRecharges_AtFrozenMultiplier()
    {
        var alert = Alert();
        await Service().ReconcileAsync(TenantId, alert, Target(30, 1m), null, default);

        await Service().ReconcileAsync(TenantId, alert, Target(60, 3m), null, default);

        _written.Select(a => (a.ChangeType, a.HoursChanged)).Should().Equal(
            (LeaveBalanceChangeTypes.Deduction, -0.5m), (LeaveBalanceChangeTypes.Adjustment, 0.5m), (LeaveBalanceChangeTypes.Deduction, -1m));
        _entitlement!.UsedHours.Should().Be(1m);
        _deductions[alert.Id].Multiplier.Should().Be(1m);
    }

    [Fact]
    public async Task NullTarget_RefundsACharge()
    {
        var alert = Alert();
        await Service().ReconcileAsync(TenantId, alert, Target(30), null, default);

        await Service().ReconcileAsync(TenantId, alert, null, null, default);

        _entitlement!.UsedHours.Should().Be(0m);
        _written.Last().Reason.Should().Be("Refund: late clock-in 19 Oct");
        _deductions[alert.Id].Status.Should().Be(AttendanceAlertDeductionStatuses.Refunded);
    }

    [Fact]
    public async Task NullTarget_WithoutADeduction_DoesNothing()
    {
        await Service().ReconcileAsync(TenantId, Alert(), null, null, default);

        _deductions.Should().BeEmpty();
        _written.Should().BeEmpty();
    }

    [Fact]
    public async Task RefundedRow_ChargesAgainWhenTargetReturns()
    {
        var alert = Alert();
        await Service().ReconcileAsync(TenantId, alert, Target(30), null, default);
        await Service().ReconcileAsync(TenantId, alert, null, null, default);

        await Service().ReconcileAsync(TenantId, alert, Target(30), null, default);

        _entitlement!.UsedHours.Should().Be(0.5m);
        _deductions[alert.Id].Status.Should().Be(AttendanceAlertDeductionStatuses.Charged);
        _written.Should().HaveCount(3);
    }
}
