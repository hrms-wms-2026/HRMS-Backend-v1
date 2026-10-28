namespace ONEVO.Application.Features.TimeAttendance.Team.Services;

/// <summary>One covered employee's widget bucket (TeamAttendanceBuckets) for every date in the requested range.</summary>
public sealed record TeamAttendanceEmployeeDays(
    Guid EmployeeId, Guid? DepartmentId, IReadOnlyDictionary<DateOnly, string> BucketByDate);

/// <summary>Batched AttendancePeriodCalculator.Classify for a whole team: the same inputs as
/// IEmployeeAttendancePeriodReader, loaded once for every employee instead of once per employee.</summary>
public interface ITeamAttendancePeriodReader
{
    Task<IReadOnlyList<TeamAttendanceEmployeeDays>> LoadAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken ct = default);
}
