namespace ONEVO.Application.Features.Calendar.Services;

public interface ICalendarNotificationSender
{
    /// <summary>In-app notification + invite email to each newly-added participant.</summary>
    Task NotifyParticipantsAddedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct = default);

    /// <summary>In-app notification only (no email) to each participant that an event changed.</summary>
    Task NotifyEventUpdatedAsync(
        Guid tenantId, string eventTitle, IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct = default);

    /// <summary>In-app notification only (no email) to each participant that an event was cancelled.</summary>
    Task NotifyEventCancelledAsync(
        Guid tenantId, string eventTitle, IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct = default);

    /// <summary>In-app notification only, to the event organizer, that a participant requested help resolving a conflict.</summary>
    Task NotifyResolutionRequestedAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, string reason, CancellationToken ct = default);

    /// <summary>In-app notification only, to the event organizer, that a participant nominated a replacement.</summary>
    Task NotifyReplacementNominatedAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, string nomineeName, CancellationToken ct = default);
}
