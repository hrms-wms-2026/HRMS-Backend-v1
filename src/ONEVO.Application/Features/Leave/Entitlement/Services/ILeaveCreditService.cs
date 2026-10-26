namespace ONEVO.Application.Features.Leave.Entitlement.Services;

public sealed record LeaveCreditOutcome(
    Guid EmployeeId,
    Guid LeaveTypeId,
    decimal HoursWritten,
    decimal ExpectedCreditedHours,
    string? SkipReason);

public interface ILeaveCreditService
{
    Task<IReadOnlyList<LeaveCreditOutcome>> CreditAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        int year,
        DateOnly asOf,
        bool dryRun,
        Guid? actorId,
        CancellationToken ct);

    Task CloseYearAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        int year,
        Guid? actorId,
        CancellationToken ct);

    Task ExpireCarryAsync(Guid tenantId, int year, DateOnly asOf, CancellationToken ct);
}
