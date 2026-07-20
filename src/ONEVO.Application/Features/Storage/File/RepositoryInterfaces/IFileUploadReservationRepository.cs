using ONEVO.Domain.Features.Storage.File.Entities;

namespace ONEVO.Application.Features.Storage.File.RepositoryInterfaces;

public interface IFileUploadReservationRepository
{
    Task<FileUploadReservation?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task AddAsync(FileUploadReservation reservation, CancellationToken ct = default);

    /// <summary>
    /// Atomically transitions a reservation from <paramref name="fromStatus"/>
    /// to <paramref name="toStatus"/> using a single conditional UPDATE, so
    /// concurrent complete/cancel calls for the same reservation cannot both
    /// succeed. Returns false when no row matched (already transitioned, wrong
    /// tenant, or not found).
    /// </summary>
    Task<bool> TryTransitionStatusAsync(
        Guid tenantId,
        Guid reservationId,
        string fromStatus,
        string toStatus,
        Guid? completedFileRecordId,
        CancellationToken ct = default);
}
