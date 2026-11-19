using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedTimeProgress;

/// <summary>Team Progress planned vs worked, unscheduled work and workload (GET api/v1/work/led-progress/time). Period is
/// week | month (required); the filters only narrow the led scope, as for led-progress/period.</summary>
public sealed record GetLedTimeProgressQuery(
    string? Period,
    Guid? ProjectId = null,
    Guid? ObjectiveId = null) : IRequest<Result<LedTimeProgressResponse>>;
