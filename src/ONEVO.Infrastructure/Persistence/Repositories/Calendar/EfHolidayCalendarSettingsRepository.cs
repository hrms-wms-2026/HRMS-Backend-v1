using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Calendar;

public class EfHolidayCalendarSettingsRepository : IHolidayCalendarSettingsRepository
{
    private readonly ApplicationDbContext _db;

    public EfHolidayCalendarSettingsRepository(ApplicationDbContext db) => _db = db;

    public async Task<HolidayCalendarSettings?> GetByLegalEntityAsync(Guid tenantId, Guid legalEntityId, CancellationToken ct = default)
        => await _db.HolidayCalendarSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.LegalEntityId == legalEntityId, ct);

    public async Task<HolidayCalendarSettings?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.HolidayCalendarSettings.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);

    public async Task AddAsync(HolidayCalendarSettings settings, CancellationToken ct = default)
        => await _db.HolidayCalendarSettings.AddAsync(settings, ct);

    public void Update(HolidayCalendarSettings settings) => _db.HolidayCalendarSettings.Update(settings);
}
