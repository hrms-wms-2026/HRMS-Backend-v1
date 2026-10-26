using System.Globalization;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Approval.Mappers;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.TimeAttendance.Alerts;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Alerts;

public interface IAttendanceAlertDeductionRepository
{
    /// <summary>Tracked.</summary>
    Task<AttendanceAlertDeduction?> GetByAlertAsync(Guid tenantId, Guid alertId, CancellationToken ct);

    /// <summary>Read-only, keyed by alert id.</summary>
    Task<IReadOnlyDictionary<Guid, AttendanceAlertDeduction>> ListByAlertsAsync(Guid tenantId, IReadOnlyCollection<Guid> alertIds, CancellationToken ct);

    void Add(AttendanceAlertDeduction deduction);
}

/// <summary>What a late/early day should cost now: the minutes missing and the policy row they reach.</summary>
public sealed record DeductionTarget(int Minutes, ClockInLateDeductionRule Rule);

/// <summary>
/// Brings an alert's leave deduction in line with what the day should cost (null = nothing): charges, refunds, or
/// refunds and re-charges when the minutes changed - always at the multiplier frozen on the first charge. Every
/// charge and refund writes one balance audit. Never saves; the caller saves once.
/// </summary>
public sealed class AttendanceDeductionService
{
    private readonly IAttendanceAlertDeductionRepository _deductions;
    private readonly ILeaveEntitlementRepository _entitlements;
    private readonly ILeaveApprovalRepository _audits;
    private readonly IDateTimeProvider _clock;

    public AttendanceDeductionService(
        IAttendanceAlertDeductionRepository deductions, ILeaveEntitlementRepository entitlements, ILeaveApprovalRepository audits,
        IDateTimeProvider clock)
    {
        _deductions = deductions;
        _entitlements = entitlements;
        _audits = audits;
        _clock = clock;
    }

    public async Task ReconcileAsync(Guid tenantId, AttendanceAlert alert, DeductionTarget? target, Guid? actorEmployeeId, CancellationToken ct)
    {
        var deduction = await _deductions.GetByAlertAsync(tenantId, alert.Id, ct);
        if (target is null)
        {
            if (deduction?.Status == AttendanceAlertDeductionStatuses.Charged)
                await RefundAsync(tenantId, alert, deduction, ct);
            else if (deduction?.Status == AttendanceAlertDeductionStatuses.Pending)
                deduction.Status = AttendanceAlertDeductionStatuses.Refunded;
            return;
        }

        if (deduction is null)
        {
            deduction = new AttendanceAlertDeduction
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = alert.EmployeeId, AlertId = alert.Id,
                LeaveTypeId = target.Rule.TimeOffTypeId, Year = alert.WorkDate.Year, Multiplier = target.Rule.Multiplier,
                Status = AttendanceAlertDeductionStatuses.Pending, CreatedAt = _clock.UtcNow,
            };
            Price(deduction, target.Minutes);
            _deductions.Add(deduction);
            await ChargeAsync(tenantId, alert, deduction, ct);
            return;
        }

        switch (deduction.Status)
        {
            case AttendanceAlertDeductionStatuses.Charged when deduction.Minutes == target.Minutes:
                return;
            case AttendanceAlertDeductionStatuses.Charged:
                await RefundAsync(tenantId, alert, deduction, ct);
                break;
        }
        Price(deduction, target.Minutes);
        await ChargeAsync(tenantId, alert, deduction, ct);
    }

    /// <summary>Minutes × the deduction's (frozen) multiplier, in hours to 2 dp.</summary>
    private static void Price(AttendanceAlertDeduction deduction, int minutes)
    {
        deduction.Minutes = minutes;
        deduction.Hours = Math.Round(minutes * deduction.Multiplier / 60m, 2, MidpointRounding.AwayFromZero);
    }

    private async Task ChargeAsync(Guid tenantId, AttendanceAlert alert, AttendanceAlertDeduction deduction, CancellationToken ct)
    {
        var entitlement = await _entitlements.GetTrackedByEmployeeTypeYearAsync(tenantId, alert.EmployeeId, deduction.LeaveTypeId, deduction.Year, ct);
        if (entitlement is null)
        {
            deduction.Status = AttendanceAlertDeductionStatuses.Pending;
            return;
        }
        var now = _clock.UtcNow;
        entitlement.UsedHours += deduction.Hours;
        entitlement.UpdatedAt = now;
        var audit = Audit(tenantId, alert, entitlement, LeaveBalanceChangeTypes.Deduction, -deduction.Hours,
            string.Create(CultureInfo.InvariantCulture, $"{Label(alert)} {alert.WorkDate:dd MMM} · {deduction.Minutes} min × {deduction.Multiplier:0.##}"), now);
        await _audits.AddBalanceAuditAsync(audit, ct);
        deduction.Status = AttendanceAlertDeductionStatuses.Charged;
        deduction.ChargedAt = now;
        deduction.ChargeAuditId = audit.Id;
        deduction.UpdatedAt = now;
    }

    private async Task RefundAsync(Guid tenantId, AttendanceAlert alert, AttendanceAlertDeduction deduction, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var entitlement = await _entitlements.GetTrackedByEmployeeTypeYearAsync(tenantId, alert.EmployeeId, deduction.LeaveTypeId, deduction.Year, ct);
        if (entitlement is not null)
        {
            entitlement.UsedHours -= deduction.Hours;
            entitlement.UpdatedAt = now;
            var audit = Audit(tenantId, alert, entitlement, LeaveBalanceChangeTypes.Adjustment, deduction.Hours,
                string.Create(CultureInfo.InvariantCulture, $"Refund: {Label(alert).ToLowerInvariant()} {alert.WorkDate:dd MMM}"), now);
            await _audits.AddBalanceAuditAsync(audit, ct);
            deduction.RefundAuditId = audit.Id;
        }
        deduction.Status = AttendanceAlertDeductionStatuses.Refunded;
        deduction.RefundedAt = now;
        deduction.UpdatedAt = now;
    }

    private static LeaveBalanceAudit Audit(
        Guid tenantId, AttendanceAlert alert, LeaveEntitlement entitlement, string changeType, decimal hours, string reason, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = alert.EmployeeId, LeaveTypeId = entitlement.LeaveTypeId,
        ChangeType = changeType, HoursChanged = hours,
        BalanceAfter = LeaveApprovalMapper.CalculateRemaining(
            entitlement.TotalHours, entitlement.CarriedForwardHours, entitlement.UsedHours, entitlement.PendingHours),
        Reason = reason, CreatedAt = now,
    };

    public static string Label(AttendanceAlert alert) =>
        alert.SignalKey == AttendanceAlertSignals.EarlyClockOuts ? "Early clock-out" : "Late clock-in";
}
