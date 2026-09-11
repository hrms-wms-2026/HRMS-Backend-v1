using System.Net.Http.Json;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.Calendar;

public sealed class NagerHolidaysClient(HttpClient http) : INagerHolidaysClient
{
    // Shape of https://date.nager.at/api/v3/PublicHolidays/{year}/{countryCode}. There is no
    // "nationalHoliday" flag: a nationwide public holiday is Global == true with "Public" among Types
    // (Types can also be Bank/School/Optional/etc.; Global == false means it is region-scoped).
    private sealed record NagerHolidayDto(DateOnly Date, string Name, string CountryCode, bool Global, string[]? Types);

    public async Task<IReadOnlyList<NagerHoliday>> GetPublicHolidaysAsync(string countryCode, int year, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"PublicHolidays/{year}/{countryCode}", ct);
        response.EnsureSuccessStatusCode();
        var dtos = await response.Content.ReadFromJsonAsync<List<NagerHolidayDto>>(cancellationToken: ct) ?? [];
        return dtos
            .Where(d => d.Global && d.Types is not null && d.Types.Contains("Public"))
            .Select(d => new NagerHoliday(d.Date, d.Name, NationalHoliday: true))
            .ToList();
    }
}
