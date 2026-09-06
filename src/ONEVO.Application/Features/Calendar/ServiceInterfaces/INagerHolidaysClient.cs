namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

public sealed record NagerHoliday(DateOnly Date, string Name, bool NationalHoliday);

public interface INagerHolidaysClient
{
    Task<IReadOnlyList<NagerHoliday>> GetPublicHolidaysAsync(string countryCode, int year, CancellationToken ct = default);
}
