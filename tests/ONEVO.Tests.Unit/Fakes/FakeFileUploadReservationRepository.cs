using ONEVO.Application.Features.Storage.File.RepositoryInterfaces;
using ONEVO.Domain.Features.Storage.File.Entities;

namespace ONEVO.Tests.Unit.Fakes;

public sealed class FakeFileUploadReservationRepository : IFileUploadReservationRepository
{
    private readonly Dictionary<Guid, FileUploadReservation> _reservations = new();

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
