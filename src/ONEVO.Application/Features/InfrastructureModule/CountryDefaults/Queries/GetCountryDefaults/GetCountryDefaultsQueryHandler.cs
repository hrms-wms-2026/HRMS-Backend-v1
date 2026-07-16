using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.InfrastructureModule.CountryDefaults.Queries.GetCountryDefaults;

public sealed class GetCountryDefaultsQueryHandler
    : IRequestHandler<GetCountryDefaultsQuery, Result<CountryDefaultsDto>>
{
    private static readonly Dictionary<string, CountryDefaultsDto> _map = new()
    {
        ["LK"] = new("LK", "Sri Lanka", "Asia/Colombo",
            ["Asia/Colombo"], "LKR",
            [new("LKR", "Sri Lankan Rupee", "Rs")]),
        ["US"] = new("US", "United States", "America/New_York",
            ["America/New_York","America/Chicago","America/Denver","America/Los_Angeles","America/Phoenix","America/Anchorage","Pacific/Honolulu"], "USD",
            [new("USD", "US Dollar", "$")]),
        ["GB"] = new("GB", "United Kingdom", "Europe/London",
            ["Europe/London"], "GBP",
            [new("GBP", "British Pound", "£")]),
        ["IN"] = new("IN", "India", "Asia/Kolkata",
            ["Asia/Kolkata"], "INR",
            [new("INR", "Indian Rupee", "₹")]),
        ["AU"] = new("AU", "Australia", "Australia/Sydney",
            ["Australia/Sydney","Australia/Melbourne","Australia/Brisbane","Australia/Perth","Australia/Adelaide","Australia/Darwin"], "AUD",
            [new("AUD", "Australian Dollar", "A$")]),
        ["SG"] = new("SG", "Singapore", "Asia/Singapore",
            ["Asia/Singapore"], "SGD",
            [new("SGD", "Singapore Dollar", "S$")]),
        ["AE"] = new("AE", "United Arab Emirates", "Asia/Dubai",
            ["Asia/Dubai"], "AED",
            [new("AED", "UAE Dirham", "د.إ")]),
        ["DE"] = new("DE", "Germany", "Europe/Berlin",
            ["Europe/Berlin"], "EUR",
            [new("EUR", "Euro", "€")]),
        ["FR"] = new("FR", "France", "Europe/Paris",
            ["Europe/Paris"], "EUR",
            [new("EUR", "Euro", "€")]),
        ["CA"] = new("CA", "Canada", "America/Toronto",
            ["America/Toronto","America/Vancouver","America/Winnipeg","America/Halifax"], "CAD",
            [new("CAD", "Canadian Dollar", "C$")]),
    };

    public Task<Result<CountryDefaultsDto>> Handle(
        GetCountryDefaultsQuery request, CancellationToken ct)
    {
        var code = request.CountryCode.ToUpperInvariant();
        return _map.TryGetValue(code, out var defaults)
            ? Task.FromResult(Result<CountryDefaultsDto>.Success(defaults))
            : Task.FromResult(Result<CountryDefaultsDto>.NotFound(
                $"Country '{request.CountryCode}' is not in the supported country list."));
    }
}
