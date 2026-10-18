namespace ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;

public sealed record MeetingApplicationComplianceItem(
    Guid EventId,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int RequiredSeconds,
    int MeetingApplicationSeconds,
    decimal UsagePercentage,
    bool IsCompliant);

public interface IMeetingComplianceReadRepository
{
    Task<IReadOnlyList<MeetingApplicationComplianceItem>> ListAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        CancellationToken ct);
}
