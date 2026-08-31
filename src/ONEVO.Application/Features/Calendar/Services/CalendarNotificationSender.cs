using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;

namespace ONEVO.Application.Features.Calendar.Services;

public sealed class CalendarNotificationSender(IOutboxWriter outboxWriter, IEmployeeRepository employees) : ICalendarNotificationSender
{
    public async Task NotifyParticipantsAddedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct = default)
    {
        foreach (var employeeId in employeeIds)
        {
            var employee = await employees.GetByIdAsync(tenantId, employeeId, ct);
            if (employee is null) continue;

            await outboxWriter.EnqueueAsync(
                OutboxMessageTypes.WorkNotification,
                new WorkNotificationPayload(
                    tenantId, employee.UserId, "calendar_event_participant_added",
                    new Dictionary<string, string> { ["organizerName"] = organizerName, ["eventTitle"] = eventTitle, ["eventDate"] = startDate.ToString("u") },
                    "calendar_event", null),
                tenantId, ct);

            await outboxWriter.EnqueueAsync(
                OutboxMessageTypes.CalendarEventInviteEmail,
                new CalendarEventInviteEmailPayload(tenantId, employee.Email, $"{employee.FirstName} {employee.LastName}", eventTitle, startDate, location, organizerName),
                tenantId, ct);
        }
    }

    public async Task NotifyEventUpdatedAsync(
        Guid tenantId, string eventTitle, IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct = default)
        => await NotifyInAppOnlyAsync(tenantId, "calendar_event_updated", eventTitle, employeeIds, organizerName, ct);

    public async Task NotifyEventCancelledAsync(
        Guid tenantId, string eventTitle, IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct = default)
        => await NotifyInAppOnlyAsync(tenantId, "calendar_event_cancelled", eventTitle, employeeIds, organizerName, ct);

    private async Task NotifyInAppOnlyAsync(
        Guid tenantId, string templateCode, string eventTitle, IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct)
    {
        foreach (var employeeId in employeeIds)
        {
            var employee = await employees.GetByIdAsync(tenantId, employeeId, ct);
            if (employee is null) continue;

            await outboxWriter.EnqueueAsync(
                OutboxMessageTypes.WorkNotification,
                new WorkNotificationPayload(
                    tenantId, employee.UserId, templateCode,
                    new Dictionary<string, string> { ["organizerName"] = organizerName, ["eventTitle"] = eventTitle },
                    "calendar_event", null),
                tenantId, ct);
        }
    }
}
