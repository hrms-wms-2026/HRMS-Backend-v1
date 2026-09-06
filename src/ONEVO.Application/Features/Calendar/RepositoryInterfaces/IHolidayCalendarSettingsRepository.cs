using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.RepositoryInterfaces;

public interface IHolidayCalendarSettingsRepository
{
    Task<HolidayCalendarSettings?> GetByLegalEntityAsync(Guid tenantId, Guid legalEntityId, CancellationToken ct = default);
    Task<HolidayCalendarSettings?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task AddAsync(HolidayCalendarSettings settings, CancellationToken ct = default);
    void Update(HolidayCalendarSettings settings);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
