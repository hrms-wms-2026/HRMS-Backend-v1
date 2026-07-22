using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Storage.File.RepositoryInterfaces;
using ONEVO.Domain.Features.Storage.File.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Storage.File;

public sealed class EfFileUploadReservationRepository : IFileUploadReservationRepository
{
    private readonly ApplicationDbContext _db;

    public EfFileUploadReservationRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<FileUploadReservation?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return _db.FileUploadReservations.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);
    }

    public async Task AddAsync(FileUploadReservation reservation, CancellationToken ct = default)
    {
        await _db.FileUploadReservations.AddAsync(reservation, ct);
    }

    public async Task<bool> TryCompleteUploadAsync(
        FileUploadReservation reservation,
        FileRecord fileRecord,
        DateTimeOffset completedAt,
        CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var reservationRows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE file_upload_reservations
            SET status = {FileUploadReservationStatus.Completed},
                updated_at = {completedAt}
            WHERE id = {reservation.Id}
              AND tenant_id = {reservation.TenantId}
              AND status = {FileUploadReservationStatus.Active}
              AND expires_at > {completedAt}
        ", ct);

        if (reservationRows == 0)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        await _db.FileRecords.AddAsync(fileRecord, ct);
        await _db.SaveChangesAsync(ct);

        var linkRows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE file_upload_reservations
            SET completed_file_record_id = {fileRecord.Id},
                updated_at = {completedAt}
            WHERE id = {reservation.Id}
              AND tenant_id = {reservation.TenantId}
              AND status = {FileUploadReservationStatus.Completed}
        ", ct);

        var quotaRows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE tenant_storage_stats
            SET reserved_r2_bytes = reserved_r2_bytes - {reservation.ReservedBytes},
                used_r2_bytes = used_r2_bytes + {reservation.ReservedBytes},
                updated_at = {completedAt}
            WHERE tenant_id = {reservation.TenantId}
              AND reserved_r2_bytes >= {reservation.ReservedBytes}
        ", ct);

        if (linkRows != 1 || quotaRows != 1)
        {
            throw new InvalidOperationException("File upload completion could not update its reservation and quota atomically.");
        }

        await transaction.CommitAsync(ct);
        reservation.Status = FileUploadReservationStatus.Completed;
        reservation.CompletedFileRecordId = fileRecord.Id;
        reservation.UpdatedAt = completedAt;
        return true;
    }

    public async Task<bool> TryTransitionStatusAsync(
        Guid tenantId,
        Guid reservationId,
        string fromStatus,
        string toStatus,
        Guid? completedFileRecordId,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE file_upload_reservations
            SET status = {toStatus},
                completed_file_record_id = {completedFileRecordId},
                updated_at = {now}
            WHERE id = {reservationId}
              AND tenant_id = {tenantId}
              AND status = {fromStatus}
        ", ct);

        return rowsAffected > 0;
    }
}
