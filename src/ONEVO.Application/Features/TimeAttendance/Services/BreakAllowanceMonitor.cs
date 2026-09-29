using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Services;

public sealed record BreakAllowanceSnapshot(
    int? AllowanceMinutes,
    int CompletedMinutes,
    int UsedMinutes,
    bool CanStartBreak,
    bool Exceeded);

/// <summary>
/// Compares today's break time with the company break allowance and records one tray
/// notification per local day once the allowance is exceeded.
/// </summary>
public sealed class BreakAllowanceMonitor(
    IAttendanceReadRepository attendance,
    INotificationRepository? notifications = null)
{
    public static BreakAllowanceSnapshot Evaluate(int? allowanceMinutes, int completedMinutes, int usedMinutes)
    {
        var exceeded = allowanceMinutes is int allowance && usedMinutes > allowance;
        var canStart = allowanceMinutes is null || usedMinutes < allowanceMinutes.Value;
        return new(allowanceMinutes, completedMinutes, usedMinutes, canStart, exceeded);
    }

    public async Task<BreakAllowanceSnapshot> ObserveAsync(AttendanceTodayContext context, CancellationToken ct)
    {
        var breaks = await attendance.ListBreaksAsync(
            context.Employee.TenantId,
            context.Employee.Id,
            context.LocalDayWindow.Start,
            context.LocalDayWindow.End,
            ct) ?? [];

        var used = AttendanceTodayStateService.CalculateBreakUsage(breaks, context.LocalDayWindow, context.LocalNow);
        var completed = CompletedMinutes(breaks, context.LocalDayWindow);
        var snapshot = Evaluate(context.LegalEntity.BreakDurationMinutes, completed, used);

        if (snapshot.Exceeded && notifications is not null)
            await NotifyOnceAsync(context, snapshot, ct);

        return snapshot;
    }

    private async Task NotifyOnceAsync(
        AttendanceTodayContext context, BreakAllowanceSnapshot snapshot, CancellationToken ct)
    {
        var since = context.LocalDayWindow.Start;
        if (await notifications!.ExistsRecentAsync(
                context.Employee.TenantId,
                context.Employee.Id,
                NotificationType.BreakAllowanceExceeded,
                since,
                ct))
            return;

        var allowance = snapshot.AllowanceMinutes!.Value;
        await notifications.AddAsync(new Notification
        {
            Id = Guid.NewGuid(),
            TenantId = context.Employee.TenantId,
            EmployeeId = context.Employee.Id,
            Type = NotificationType.BreakAllowanceExceeded,
            Title = "Break time exceeded",
            Message = $"Your break is longer than the {allowance} minute allowance. You can't start another break today.",
            MetadataJson = $$"""{"allowanceMinutes":{{allowance}},"usedMinutes":{{snapshot.UsedMinutes}}}""",
            CreatedAt = context.UtcNow
        }, ct);
        await notifications.SaveChangesAsync(ct);
    }

    private static int CompletedMinutes(IReadOnlyList<BreakRecord> records, AttendanceLocalDayWindow window)
    {
        return records.Sum(record =>
        {
            if (record.BreakEnd is not DateTimeOffset end)
                return 0;
            var start = record.BreakStart < window.Start ? window.Start : record.BreakStart;
            if (end > window.End)
                end = window.End;
            return end <= start ? 0 : (int)Math.Max(0, (end - start).TotalMinutes);
        });
    }
}
