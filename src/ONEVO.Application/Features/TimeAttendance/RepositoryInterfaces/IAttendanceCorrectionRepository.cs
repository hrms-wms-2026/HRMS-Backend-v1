using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

public interface IAttendanceCorrectionRepository
{
    Task AddAsync(AttendanceCorrection correction, CancellationToken ct = default);
    Task<AttendanceCorrection?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<AttendanceCorrection?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<(IReadOnlyList<AttendanceCorrection> Items, int TotalCount)> ListMyAsync(
        Guid tenantId, Guid employeeId, DateOnly? from, DateOnly? to, string? status,
        int skip, int take, CancellationToken ct = default);
    Task<IReadOnlyList<AttendanceCorrection>> ListApprovalInboxAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> employeeIds,
        DateOnly? from, DateOnly? to, string? status, CancellationToken ct = default);
    Task<bool> HasPendingForRecordAsync(
        Guid tenantId, Guid employeeId, Guid? attendanceRecordId, string correctionType, CancellationToken ct = default);
    /// <summary>The employee's requests of any status created in [from, to) - the inbox case history.</summary>
    Task<IReadOnlyList<AttendanceCorrection>> ListForEmployeeCreatedAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
