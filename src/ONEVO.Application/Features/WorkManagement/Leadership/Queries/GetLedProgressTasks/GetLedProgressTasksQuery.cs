using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedProgressTasks;

/// <summary>Team Progress detail - the top-level tasks behind the hours, in the caller's led scope
/// (GET api/v1/work/led-progress/tasks). PageSize is clamped to 1..100, PageNumber to &gt;= 1.
/// Period (all|week|month, default all) keeps only tasks due in that window - see LedProgressPeriod.</summary>
public sealed record GetLedProgressTasksQuery(
    int PageNumber = 1,
    int PageSize = LedProgressQueryOptions.DefaultPageSize,
    string? Search = null,
    string? Status = null,
    Guid? ProjectId = null,
    Guid? ObjectiveId = null,
    Guid? EmployeeId = null,
    string? Sort = null,
    string? Direction = null,
    string? Period = null) : IRequest<Result<PagedResult<LedProgressTaskItem>>>;
