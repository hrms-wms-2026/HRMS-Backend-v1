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
}

public interface IAttendanceAlertRepository : IAttendanceAlertResolutionLookup
{
    /// <summary>Every live alert (open and resolved) of one employee whose work date is in [from, to]; tracked.</summary>
    Task<List<AttendanceAlert>> ListForEmployeeAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<AttendanceAlert?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct);

    Task<AttendanceAlert?> FindOpenAsync(Guid tenantId, Guid employeeId, DateOnly workDate, string signalKey, CancellationToken ct);

    /// <summary>Open alerts of one legal entity, oldest day first, at most <paramref name="take"/> (the Inbox received list).</summary>
    Task<IReadOnlyList<AttendanceAlert>> ListOpenAsync(Guid tenantId, Guid legalEntityId, int take, CancellationToken ct);

    void Add(AttendanceAlert alert);
    void Remove(AttendanceAlert alert);
    Task SaveChangesAsync(CancellationToken ct);
}
