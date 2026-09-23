using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;

public sealed class EfDailyWorkLocationConfirmationRepository(ApplicationDbContext db)
    : IDailyWorkLocationConfirmationRepository
{
    public Task<DailyWorkLocationConfirmation?> GetForDateAsync(
        Guid tenantId, Guid employeeId, DateOnly workDate, CancellationToken ct = default) =>
        db.DailyWorkLocationConfirmations.FirstOrDefaultAsync(
            c => c.TenantId == tenantId && c.EmployeeId == employeeId && c.WorkDate == workDate, ct);

    public async Task UpsertAsync(DailyWorkLocationConfirmation confirmation, CancellationToken ct = default)
    {
        var existing = await db.DailyWorkLocationConfirmations.FirstOrDefaultAsync(
            c => c.TenantId == confirmation.TenantId
                && c.EmployeeId == confirmation.EmployeeId
                && c.WorkDate == confirmation.WorkDate, ct);

        if (existing is null)
        {
            await db.DailyWorkLocationConfirmations.AddAsync(confirmation, ct);
            return;
        }

        existing.LocationType = confirmation.LocationType;
        existing.Latitude = confirmation.Latitude;
        existing.Longitude = confirmation.Longitude;
        existing.AccuracyMeters = confirmation.AccuracyMeters;
        existing.ConfirmedAt = confirmation.ConfirmedAt;
    }
}
