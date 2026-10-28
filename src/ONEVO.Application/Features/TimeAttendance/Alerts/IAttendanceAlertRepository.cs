using ONEVO.Domain.Features.TimeAttendance.Alerts;

namespace ONEVO.Application.Features.TimeAttendance.Alerts;

/// <summary>
/// Which attendance days a reviewer took out of the counts (plan E1): per signal, the dates whose alert was resolved as
/// excused or converted to leave. The period reader hands them to <c>AttendancePeriodCalculator</c>, so the overview,
/// KPI strip, signals, compare-month and My Team all drop together.
/// </summary>
public interface IAttendanceAlertResolutionLookup
{
    Task<IReadOnlyDictionary<string, IReadOnlySet<DateOnly>>> ExcusedDatesAsync(
        Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>Batched ExcusedDatesAsync, keyed by employee; employees with nothing excused are left out.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlySet<DateOnly>>>> ExcusedDatesForEmployeesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken ct);
}

public interface IAttendanceAlertRepository : IAttendanceAlertResolutionLookup
{
    /// <summary>Every live alert (open and resolved) of one employee whose work date is in [from, to]; tracked.</summary>
    Task<List<AttendanceAlert>> ListForEmployeeAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<AttendanceAlert?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct);

    Task<AttendanceAlert?> FindOpenAsync(Guid tenantId, Guid employeeId, DateOnly workDate, string signalKey, CancellationToken ct);

    /// <summary>The employees of one legal entity that have an open alert (the candidates the Inbox scopes first).</summary>
    Task<IReadOnlyList<Guid>> ListOpenEmployeeIdsAsync(Guid tenantId, Guid legalEntityId, CancellationToken ct);

    /// <summary>Open alerts of the given employees in one legal entity, newest day first, at most <paramref name="take"/>
    /// (the Inbox received list; scoped before the cap so other teams' backlog never crowds a reviewer's team out).</summary>
    Task<IReadOnlyList<AttendanceAlert>> ListOpenAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> employeeIds, int take, CancellationToken ct);

    void Add(AttendanceAlert alert);
    void Remove(AttendanceAlert alert);
    Task SaveChangesAsync(CancellationToken ct);
}
