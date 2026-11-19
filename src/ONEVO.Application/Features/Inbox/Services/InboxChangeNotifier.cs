using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Inbox.OutboxHandlers;

namespace ONEVO.Application.Features.Inbox.Services;

/// <summary>
/// Tells the given employees' open inboxes to refresh one item, after the change commits (outbox, handled by
/// InboxChangedOutboxHandler). Module paths that create or change items outside the Inbox (leave submitted,
/// correction requested, exception detected, ...) call it next to their own notification enqueue.
/// The outbox only adds to the DbContext: call it BEFORE the module's SaveChangesAsync, or nothing is sent.
/// </summary>
public interface IInboxChangeNotifier
{
    Task EnqueueAsync(Guid tenantId, IEnumerable<Guid> employeeIds, ItemRef item, CancellationToken ct = default);
}

public sealed class OutboxInboxChangeNotifier : IInboxChangeNotifier
{
    private readonly IOutboxWriter _outbox;

    public OutboxInboxChangeNotifier(IOutboxWriter outbox) => _outbox = outbox;

    public async Task EnqueueAsync(Guid tenantId, IEnumerable<Guid> employeeIds, ItemRef item, CancellationToken ct = default)
    {
        var people = employeeIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (people.Count == 0)
            return;
        await _outbox.EnqueueAsync(
            OutboxMessageTypes.InboxChanged, new InboxChangedPayload(tenantId, people, item.Source, item.Kind, item.Id), tenantId, ct);
    }
}

/// <summary>How module items are addressed in the inbox, for code outside the item's provider.</summary>
public static class InboxRefs
{
    public static ItemRef LeaveRequest(Guid id) => new("leave", "request", id);
    public static ItemRef AttendanceCorrection(Guid id) => new("attendance", "correction", id);
    public static ItemRef AttendanceWorkModeChange(Guid id) => new("attendance", "work_mode_change", id);
    public static ItemRef AttendanceLocationChange(Guid id) => new("attendance", "location_change", id);
    public static ItemRef AttendanceDeviceChange(Guid id) => new("attendance", "device_change", id);
    public static ItemRef AttendanceAlert(Guid id) => new("attendance", "alert", id);
    public static ItemRef ExceptionCase(Guid id) => new("monitoring", "exception_case", id);
    public static ItemRef CalendarMeetingException(Guid id) => new("calendar", "meeting_exception", id);
}
