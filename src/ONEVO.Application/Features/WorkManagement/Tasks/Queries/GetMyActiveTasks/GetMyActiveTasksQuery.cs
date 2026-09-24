using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyActiveTasks;

/// <summary>Overdue and near-term (due within UpcomingDays) not-yet-complete tasks assigned
/// to the current employee, for the My Tasks dashboard widget.</summary>
public sealed record GetMyActiveTasksQuery(int UpcomingDays = 7) : IRequest<Result<MyTasksResponse>>;
