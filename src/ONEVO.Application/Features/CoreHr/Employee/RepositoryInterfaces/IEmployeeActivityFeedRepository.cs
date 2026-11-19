namespace ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

/// <summary>One thing an employee did, from a source table. Target is a short subject (a task,
/// a leave type, ...); Detail an optional qualifier ("40% → 60%"). Never contains free text the
/// employee typed (comments, reasons).</summary>
public sealed record EmployeeActivityRow(string Kind, Guid SourceId, DateTimeOffset At, string? Target, string? Detail);

public interface IEmployeeActivityFeedRepository
{
    /// <summary>The employee's most recent actions across attendance, leave and Work Management,
    /// newest first, at most <paramref name="take"/>, all strictly older than <paramref name="before"/>
    /// when it is given. <paramref name="userId"/> is the employee's user id (used for "task created").</summary>
    Task<IReadOnlyList<EmployeeActivityRow>> ListAsync(
        Guid tenantId, Guid employeeId, Guid userId, DateTimeOffset? before, int take, CancellationToken ct = default);
}
