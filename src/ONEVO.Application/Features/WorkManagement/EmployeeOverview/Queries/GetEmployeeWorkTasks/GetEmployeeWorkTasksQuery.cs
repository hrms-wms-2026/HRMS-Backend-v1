using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeWorkTasks;

public sealed record GetEmployeeWorkTasksQuery(Guid EmployeeId, DateOnly? From, DateOnly? To)
    : IRequest<Result<EmployeeWorkTasksResponse>>;
