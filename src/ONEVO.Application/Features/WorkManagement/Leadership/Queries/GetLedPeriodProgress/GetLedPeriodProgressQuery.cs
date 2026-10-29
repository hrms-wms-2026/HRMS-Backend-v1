using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedPeriodProgress;

/// <summary>Team Progress period switch - team totals of the tasks due this week or this month in
/// the caller's led scope (GET api/v1/work/led-progress/period). Period is week | month
/// (case-insensitive); "all" is served by GET led-progress itself.</summary>
public sealed record GetLedPeriodProgressQuery(
    string? Period,
    Guid? ProjectId = null,
    Guid? ObjectiveId = null) : IRequest<Result<LedPeriodProgressResponse>>;
