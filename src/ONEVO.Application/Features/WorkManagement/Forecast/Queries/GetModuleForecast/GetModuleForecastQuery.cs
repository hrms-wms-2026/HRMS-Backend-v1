using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Forecast.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Forecast.Queries.GetModuleForecast;

/// <param name="Status">on_track | raise_rate | overtime | no_history; null = all.</param>
/// <param name="Sort">overtime (default, most first) | requiredRate (highest first) | endDate (soonest first) | name.</param>
/// <param name="Search">Module title, path or project name, case-insensitive.</param>
public sealed record GetModuleForecastQuery(
    string? Status = null, Guid? ProjectId = null, string? Sort = null, string? Search = null, int Page = 1, int PageSize = 10)
    : IRequest<Result<ModuleForecastResponse>>;

public static class ForecastQueryOptions
{
    /// <summary>The popup's CSV export asks for every row in one page.</summary>
    public const int MaxPageSize = 500;

    public static (int Page, int PageSize) Clamp(int page, int pageSize)
        => (Math.Max(1, page), pageSize < 1 ? 10 : Math.Min(MaxPageSize, pageSize));
}
