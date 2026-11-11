using System.Globalization;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;

namespace ONEVO.Application.Features.WorkManagement.Planner.Services;

/// <summary>What a meeting delegate did to a block on the principal's planner.</summary>
public static class PlannerDelegateActions
{
    public const string Planned = "planned";
    public const string Moved = "moved";
    public const string Removed = "removed";
}

/// <summary>
/// Delegate task planning (2026-11-11), "Notifications": tells the principal, in-app, about every block their delegate
/// writes on their planner. Every such notice goes through <see cref="BlockWrittenAsync"/>, so grouping them later is a
/// change here only.
/// </summary>
public interface IPlannerDelegateNotifier
{
    /// <summary>Queues the notice on the outbox in the caller's unit of work, so it goes out only if the write commits.</summary>
    /// <param name="action">One of <see cref="PlannerDelegateActions"/>; the new time for a move, the old one for a remove.</param>
    Task BlockWrittenAsync(Guid tenantId, Guid principalEmployeeId, Guid delegateEmployeeId, string action, Guid blockId,
        string taskTitle, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
}

public sealed class PlannerDelegateNotifier : IPlannerDelegateNotifier
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private readonly IOutboxWriter _outbox;
    private readonly IEmployeeRepository _employees;
    private readonly ICalendarTimezoneResolver _timezones;

    public PlannerDelegateNotifier(IOutboxWriter outbox, IEmployeeRepository employees, ICalendarTimezoneResolver timezones)
    {
        _outbox = outbox;
        _employees = employees;
        _timezones = timezones;
    }

    public async Task BlockWrittenAsync(Guid tenantId, Guid principalEmployeeId, Guid delegateEmployeeId, string action, Guid blockId,
        string taskTitle, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var principal = await _employees.GetByIdAsync(tenantId, principalEmployeeId, ct);
        if (principal is null)
            return;
        var writer = await _employees.GetByIdAsync(tenantId, delegateEmployeeId, ct);
        var zoneId = await _timezones.ResolveForUserAsync(tenantId, principal.UserId, ct);
        await _outbox.EnqueueAsync(OutboxMessageTypes.WorkNotification,
            new WorkNotificationPayload(tenantId, principal.UserId, CodeFor(action), new Dictionary<string, string>
            {
                ["delegateName"] = writer is null ? string.Empty : $"{writer.FirstName} {writer.LastName}".Trim(),
                ["taskTitle"] = taskTitle,
                ["when"] = When(start, end, zoneId),
            }, "planned_task_block", blockId, EventId: Guid.NewGuid()),
            tenantId, ct);
    }

    private static string CodeFor(string action) => action switch
    {
        PlannerDelegateActions.Planned => "planner_block_planned_for_you",
        PlannerDelegateActions.Moved => "planner_block_moved_for_you",
        PlannerDelegateActions.Removed => "planner_block_removed_for_you",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown planner delegate action."),
    };

    /// <summary>"Tuesday, November 10, 3:15 PM – 4:15 PM (Asia/Colombo)" in the principal's zone; UTC for a zone this
    /// machine does not know. Never raw UTC instants.</summary>
    private static string When(DateTimeOffset start, DateTimeOffset end, string zoneId)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var zone))
        {
            zone = TimeZoneInfo.Utc;
            zoneId = "UTC";
        }

        var from = TimeZoneInfo.ConvertTime(start, zone);
        var to = TimeZoneInfo.ConvertTime(end, zone);
        return $"{from.ToString("dddd, MMMM d, h:mm tt", English)} – {to.ToString("h:mm tt", English)} ({zoneId})";
    }
}
