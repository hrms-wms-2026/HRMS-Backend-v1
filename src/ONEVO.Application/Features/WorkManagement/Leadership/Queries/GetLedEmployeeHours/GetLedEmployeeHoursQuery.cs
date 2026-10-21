using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedEmployeeHours;

/// <summary>Team Progress detail - per-employee estimated hours in the caller's led scope
/// (GET api/v1/work/led-progress/employees). PageSize is clamped to 1..100, PageNumber to &gt;= 1.
/// Period (all|week|month, default all) keeps only tasks due in that window - see LedProgressPeriod.</summary>
public sealed record GetLedEmployeeHoursQuery(
    int PageNumber = 1,
    int PageSize = LedProgressQueryOptions.DefaultPageSize,
    string? Search = null,
    Guid? ProjectId = null,
    Guid? ObjectiveId = null,
    string? Sort = null,
    string? Direction = null,
    string? Period = null) : IRequest<Result<LedEmployeeHoursPage>>;
