using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Services.Calendar;

public sealed class NagerHolidaysProvider(INagerHolidaysClient client, IHolidayCalendarSettingsRepository settingsRepo)
    : ILeaveCalendarHolidayProvider
{
    public async Task<IReadOnlyList<LeaveCalendarHoliday>> ListHolidaysAsync(
        Guid tenantId, IReadOnlyCollection<Guid> legalEntityIds, DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
    {
        var result = new List<LeaveCalendarHoliday>();
        foreach (var legalEntityId in legalEntityIds)
        {
            var settings = await settingsRepo.GetByLegalEntityAsync(tenantId, legalEntityId, ct);
            if (settings is null || !settings.HolidaySyncEnabled) continue;
            var holidays = await FetchInRangeAsync(settings.EffectiveCountryCode, startDate, endDate, ct);
            result.AddRange(holidays.Select(h => new LeaveCalendarHoliday(
                h.Date, h.Name, legalEntityId, null, HolidayCalendarProviders.NagerHolidays)));
        }
        return result;
    }

    private async Task<IReadOnlyList<NagerHoliday>> FetchInRangeAsync(string countryCode, DateOnly startDate, DateOnly endDate, CancellationToken ct)
    {
        var years = Enumerable.Range(startDate.Year, endDate.Year - startDate.Year + 1);
        var all = new List<NagerHoliday>();
        foreach (var year in years)
        {
            var holidays = await client.GetPublicHolidaysAsync(countryCode, year, ct);
            all.AddRange(holidays.Where(h => h.Date >= startDate && h.Date <= endDate));
        }
        return all;
    }
}
