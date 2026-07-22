using ONEVO.Application.Features.Storage.File.RepositoryInterfaces;
using ONEVO.Domain.Features.Storage.File.Entities;

namespace ONEVO.Tests.Unit.Fakes;

public sealed class FakeFileUploadReservationRepository : IFileUploadReservationRepository
{
    private readonly Dictionary<Guid, FileUploadReservation> _reservations = new();
    public int AtomicCompletionCount { get; private set; }
    public bool ShouldFailAtomicCompletion { get; set; }

    public Task<bool> TryCompleteUploadAsync(
        FileUploadReservation reservation,
        ONEVO.Domain.Features.Storage.File.Entities.FileRecord fileRecord,
        DateTimeOffset completedAt,
        CancellationToken ct = default)
    {
        if (ShouldFailAtomicCompletion)
        {
            throw new InvalidOperationException("simulated metadata completion failure");
        }

        if (!_reservations.TryGetValue(reservation.Id, out var stored)
            || stored.TenantId != reservation.TenantId
            || stored.Status != FileUploadReservationStatus.Active
            || stored.ExpiresAt <= completedAt)
        {
            return Task.FromResult(false);
        }

        stored.Status = FileUploadReservationStatus.Completed;
        stored.CompletedFileRecordId = fileRecord.Id;
        stored.UpdatedAt = completedAt;
        AtomicCompletionCount++;
        return Task.FromResult(true);
    }

    public Task<FileUploadReservation?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        if (_reservations.TryGetValue(id, out var reservation) && reservation.TenantId == tenantId)
        {
            return Task.FromResult<FileUploadReservation?>(reservation);
        }

        return Task.FromResult<FileUploadReservation?>(null);
    }

    public Task AddAsync(FileUploadReservation reservation, CancellationToken ct = default)
    {
        _reservations[reservation.Id] = reservation;
        return Task.CompletedTask;
    }

    public Task<bool> TryTransitionStatusAsync(
        Guid tenantId,
        Guid reservationId,
        string fromStatus,
        string toStatus,
        Guid? completedFileRecordId,
        CancellationToken ct = default)
    {
        if (!_reservations.TryGetValue(reservationId, out var reservation))
        {
            return Task.FromResult(false);
        }

        if (reservation.TenantId != tenantId || reservation.Status != fromStatus)
        {
            return Task.FromResult(false);
        }

        reservation.Status = toStatus;
        reservation.CompletedFileRecordId = completedFileRecordId;
        reservation.UpdatedAt = DateTimeOffset.UtcNow;
        return Task.FromResult(true);
    }
}
