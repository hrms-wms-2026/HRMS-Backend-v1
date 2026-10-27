namespace ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;

/// <summary>One meeting in the monitoring compliance view.</summary>
/// <param name="EventId">The legacy calendar event's id, or a task meeting's occurrence task id.</param>
/// <param name="Title">The meeting's title, or "Private meeting" when it is private (spec D17).</param>
/// <param name="IsPrivate">True when the meeting is private: its title, organizer, agenda, participants and link are hidden.</param>
public sealed record MeetingApplicationComplianceItem(
    Guid EventId,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int RequiredSeconds,
    int MeetingApplicationSeconds,
    decimal UsagePercentage,
    bool IsCompliant)
{
    /// <summary>True when the meeting is private: its title, organizer, agenda, participants and link are hidden.
    /// An init property, so the constructor keeps its shape for every existing caller.</summary>
    public bool IsPrivate { get; init; }
}

public interface IMeetingComplianceReadRepository
{
    Task<IReadOnlyList<MeetingApplicationComplianceItem>> ListAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        CancellationToken ct);
}
