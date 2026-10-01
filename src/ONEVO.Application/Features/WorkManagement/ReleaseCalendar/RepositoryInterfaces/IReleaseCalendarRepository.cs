using ONEVO.Domain.Features.WorkManagement.ReleaseCalendar.Entities;

namespace ONEVO.Application.Features.WorkManagement.ReleaseCalendar.RepositoryInterfaces;

public sealed record UpcomingReleaseRow(
    Guid Id,
    DateOnly ScheduledDate,
    string ReminderType,
    string? Notes,
    string VersionName,
    string ProjectName);

public interface IReleaseCalendarRepository
{
    Task AddAsync(ReleaseCalendarEntry entry, CancellationToken ct = default);

    /// <summary>Active release reminders addressed to this user, scheduled from..to (inclusive),
    /// earliest first, with the version and project names. For the employee Overview.</summary>
    Task<IReadOnlyList<UpcomingReleaseRow>> ListForRecipientAsync(
        Guid tenantId, Guid recipientUserId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
