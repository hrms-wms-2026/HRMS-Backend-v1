using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeWorkOverview;

public sealed record GetEmployeeWorkOverviewQuery(Guid EmployeeId, DateOnly? From, DateOnly? To)
    : IRequest<Result<EmployeeWorkOverviewResponse>>;
