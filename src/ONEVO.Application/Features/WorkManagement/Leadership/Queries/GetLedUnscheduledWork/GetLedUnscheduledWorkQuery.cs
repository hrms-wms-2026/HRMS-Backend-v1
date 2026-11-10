using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedUnscheduledWork;

/// <summary>Team Progress unscheduled work, paged (GET api/v1/work/led-progress/time/unscheduled). Period is week | month
/// (default week); Kind is all | no_plan | outside_slot (default all). PageSize is clamped to 1..100, PageNumber to &gt;= 1.
/// The filters only narrow the led scope, as for led-progress/time.</summary>
public sealed record GetLedUnscheduledWorkQuery(
    string? Period,
    string? Kind = null,
    int PageNumber = 1,
    int PageSize = LedProgressQueryOptions.DefaultPageSize,
    Guid? ProjectId = null,
    Guid? ObjectiveId = null) : IRequest<Result<LedUnscheduledPage>>;
