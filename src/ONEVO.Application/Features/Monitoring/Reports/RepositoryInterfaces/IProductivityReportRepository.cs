namespace ONEVO.Application.Features.Monitoring.Reports.RepositoryInterfaces;

public record ProductivityAggregate(
    int TotalActiveMinutes,
    int TotalIdleMinutes,
    int TotalMeetingMinutes,
    int ProductiveAppMinutes,
    int PersonalAppMinutes,
    int UnknownAppMinutes,
    decimal AverageActivityScore,
    int TotalWorkedMinutes,
    int TotalBreakMinutes,
    int DayCount);

public interface IProductivityReportRepository
{
    Task<ProductivityAggregate> GetEmployeeAggregateAsync(
        Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>Null if departmentId doesn't exist for this tenant.</summary>
    Task<ProductivityAggregate?> GetDepartmentAggregateAsync(
        Guid tenantId, Guid departmentId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<ProductivityAggregate> GetTenantAggregateAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>
    /// The aggregate over exactly <paramref name="employeeIds"/> - a coverage-limited caller's
    /// tenant view. An empty set is an all-zero aggregate.
    /// </summary>
    Task<ProductivityAggregate> GetEmployeesAggregateAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>
    /// The department's employees that are also in <paramref name="employeeIds"/>. Null if the
    /// department doesn't exist for this tenant.
    /// </summary>
    Task<ProductivityAggregate?> GetDepartmentAggregateForEmployeesAsync(
        Guid tenantId, Guid departmentId, IReadOnlyCollection<Guid> employeeIds,
        DateOnly from, DateOnly to, CancellationToken ct);
}
