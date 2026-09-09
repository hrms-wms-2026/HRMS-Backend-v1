using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

public interface IDailyWorkLocationConfirmationRepository
{
    Task<DailyWorkLocationConfirmation?> GetForDateAsync(
        Guid tenantId, Guid employeeId, DateOnly workDate, CancellationToken ct = default);

    /// <summary>Inserts a new row, or updates the existing row for (tenantId, employeeId, workDate)
    /// in place. Does not call SaveChanges - the caller commits via IUnitOfWork.</summary>
    Task UpsertAsync(DailyWorkLocationConfirmation confirmation, CancellationToken ct = default);
}
