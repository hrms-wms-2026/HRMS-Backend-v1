using System.Text.Json.Serialization;
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.InfrastructureModule.CountryDefaults.Queries.GetCountryDefaults;

public sealed record GetCountryDefaultsQuery(string CountryCode)
    : IRequest<Result<CountryDefaultsDto>>;

public sealed record CountryDefaultsDto(
    [property: JsonPropertyName("country_code")] string CountryCode,
    [property: JsonPropertyName("country_name")] string CountryName,
    [property: JsonPropertyName("default_timezone")] string DefaultTimezone,
    [property: JsonPropertyName("timezones")] IReadOnlyList<string> Timezones,
    [property: JsonPropertyName("default_currency")] string DefaultCurrency,
    [property: JsonPropertyName("currencies")] IReadOnlyList<CurrencyOptionDto> Currencies);

public sealed record CurrencyOptionDto(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("symbol")] string Symbol);
