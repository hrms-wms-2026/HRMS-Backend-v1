using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeDeliveryTrend;

/// <summary>To = any day in the last month of the 6-month window; null = today.</summary>
public sealed record GetEmployeeDeliveryTrendQuery(Guid EmployeeId, DateOnly? To) : IRequest<Result<EmployeeDeliveryTrendResponse>>;
