using System.Net.Http.Json;

namespace ONEVO.Infrastructure.Services.Calendar;

public sealed record NagerHoliday(DateOnly Date, string Name, bool NationalHoliday);

public interface INagerHolidaysClient
{
    Task<IReadOnlyList<NagerHoliday>> GetPublicHolidaysAsync(string countryCode, int year, CancellationToken ct = default);
}

public sealed class NagerHolidaysClient(HttpClient http) : INagerHolidaysClient
{
    private sealed record NagerHolidayDto(DateOnly Date, string Name, string CountryCode, bool NationalHoliday);

    public async Task<IReadOnlyList<NagerHoliday>> GetPublicHolidaysAsync(string countryCode, int year, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"Holidays/{countryCode}/{year}", ct);
        response.EnsureSuccessStatusCode();
        var dtos = await response.Content.ReadFromJsonAsync<List<NagerHolidayDto>>(cancellationToken: ct) ?? [];
        return dtos.Where(d => d.NationalHoliday).Select(d => new NagerHoliday(d.Date, d.Name, d.NationalHoliday)).ToList();
    }
}
