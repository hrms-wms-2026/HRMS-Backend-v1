using Microsoft.EntityFrameworkCore;
using ONEVO.Infrastructure.Persistence.SqlServer;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;

public sealed class EfAttendanceCorrectionRepository(ApplicationDbContext db) : IAttendanceCorrectionRepository
{
    public Task AddAsync(AttendanceCorrection correction, CancellationToken ct = default)
        => db.AttendanceCorrections.AddAsync(correction, ct).AsTask();

    public Task<AttendanceCorrection?> GetTrackedByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default)
        => db.AttendanceCorrections.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Id == id, ct);

    public Task<AttendanceCorrection?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default)
        => db.AttendanceCorrections.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Id == id, ct);

    public async Task<(IReadOnlyList<AttendanceCorrection> Items, int TotalCount)> ListMyAsync(
        Guid tenantId, Guid employeeId, DateOnly? from, DateOnly? to, string? status,
        int skip, int take, CancellationToken ct = default)
    {
        var query = FromDateFiltered(tenantId, employeeId, from, to);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<AttendanceCorrection>> ListApprovalInboxAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> employeeIds,
        DateOnly? from, DateOnly? to, string? status, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return Array.Empty<AttendanceCorrection>();

        var query = FromDateFiltered(tenantId, employeeIds, from, to)
            .Where(x => x.LegalEntityId == legalEntityId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        return await query.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> ListPendingEmployeeIdsAsync(
        Guid tenantId, Guid legalEntityId, CancellationToken ct = default)
        => await db.AttendanceCorrections.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.LegalEntityId == legalEntityId
                && x.Status == AttendanceCorrection.StatusPending)
            .Select(x => x.EmployeeId).Distinct().ToListAsync(ct);

    public Task<int> CountPendingAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
        => employeeIds.Count == 0 ? Task.FromResult(0) : db.AttendanceCorrections.AsNoTracking().CountAsync(x =>
            x.TenantId == tenantId && x.LegalEntityId == legalEntityId
            && employeeIds.Contains(x.EmployeeId) && x.Status == AttendanceCorrection.StatusPending, ct);

    public Task<bool> HasPendingForRecordAsync(
        Guid tenantId, Guid employeeId, Guid? attendanceRecordId, string correctionType,
        CancellationToken ct = default)
        => db.AttendanceCorrections.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenantId
            && x.EmployeeId == employeeId
            && x.AttendanceRecordId == attendanceRecordId
            && x.CorrectionType == correctionType
            && x.Status == AttendanceCorrection.StatusPending, ct);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (
            SqlServerErrors.IsUniqueViolation(ex))
        {
            throw new UniqueConstraintConflictException(ex);
        }
    }

    private IQueryable<AttendanceCorrection> FromDateFiltered(
        Guid tenantId, Guid employeeId, DateOnly? from, DateOnly? to)
        => FromDateFiltered(tenantId, new[] { employeeId }, from, to);

    private IQueryable<AttendanceCorrection> FromDateFiltered(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, DateOnly? from, DateOnly? to)
    {
        var query = db.AttendanceCorrections.AsNoTracking()
            .Where(x => x.TenantId == tenantId
                && employeeIds.Contains(x.EmployeeId));

        if (from is not null)
            query = query.Where(x => x.WorkDate >= from.Value);
        if (to is not null)
            query = query.Where(x => x.WorkDate <= to.Value);

        return query;
    }

    public async Task<IReadOnlyList<AttendanceCorrection>> ListForEmployeeCreatedAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => await db.AttendanceCorrections.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.CreatedAt >= from && x.CreatedAt < to)
            .ToListAsync(ct);
}
