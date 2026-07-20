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
