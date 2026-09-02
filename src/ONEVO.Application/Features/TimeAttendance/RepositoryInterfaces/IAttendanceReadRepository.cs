using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

public interface IAttendanceReadRepository
{
    Task<AttendanceRecord?> GetRecordAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly date,
        CancellationToken ct = default);

    Task<AttendanceRecord?> GetTrackedRecordAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly date,
        CancellationToken ct = default);

    Task<(IReadOnlyList<AttendanceRecord> Items, int TotalCount)> ListRecordsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly from,
        DateOnly to,
        int skip,
        int take,
        CancellationToken ct = default);

    Task<IReadOnlyList<BreakRecord>> ListBreaksAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);

    Task<IReadOnlyList<BreakRecord>> ListBreaksForEmployeesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);

    Task<bool> HasOpenBreakAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);

    Task<BreakRecord?> GetOpenBreakTrackedAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);

    Task<BreakRecord?> GetAnyOpenBreakTrackedAsync(
        Guid tenantId,
        Guid employeeId,
        CancellationToken ct = default);

    Task<int> SumCompletedBreakMinutesAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);

    Task AddRecordAsync(AttendanceRecord record, CancellationToken ct = default);

    Task AddBreakAsync(BreakRecord record, CancellationToken ct = default);

    Task DeleteBreakAsync(Guid breakId, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, AttendanceHistoryEmployee>> ListEmployeeIdentitiesAsync(
        Guid tenantId,
        Guid legalEntityId,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<AttendanceRecord>> ListByStatusAsync(
        Guid tenantId, DateOnly date, string status, CancellationToken ct = default);
}
