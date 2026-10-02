using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeRecentTasks;

public sealed record GetEmployeeRecentTasksQuery(Guid EmployeeId) : IRequest<Result<EmployeeRecentTasksResponse>>;
