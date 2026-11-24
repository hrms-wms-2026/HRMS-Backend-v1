using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.Caching;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Services.SharedPlatform;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Calendar;

public class EfHolidayCalendarSettingsRepository : IHolidayCalendarSettingsRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantCache _cache;

    public EfHolidayCalendarSettingsRepository(ApplicationDbContext db, ITenantCache? cache = null)
    {
        _db = db;
        _cache = cache ?? PassThroughTenantCache.Instance;
    }

    /// <summary>Read for every calendar view (holiday provider); cached per tenant and legal entity (task 22).</summary>
    public async Task<HolidayCalendarSettings?> GetByLegalEntityAsync(Guid tenantId, Guid legalEntityId, CancellationToken ct = default)
    {
        var settings = await _cache.GetOrCreateAsync(
            tenantId, ReferenceDataCacheKeys.HolidayCalendarSettings, legalEntityId.ToString("N"),
            token => _db.HolidayCalendarSettings.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.LegalEntityId == legalEntityId, token),
            ct);
        return ReferenceDataCopy.OneOrNull(settings);
    }

    public async Task<HolidayCalendarSettings?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.HolidayCalendarSettings.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);

    public async Task AddAsync(HolidayCalendarSettings settings, CancellationToken ct = default)
        => await _db.HolidayCalendarSettings.AddAsync(settings, ct);

    public void Update(HolidayCalendarSettings settings) => _db.HolidayCalendarSettings.Update(settings);
}
