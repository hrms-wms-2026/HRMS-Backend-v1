using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeDelivery;

public sealed record GetEmployeeDeliveryQuery(Guid EmployeeId, DateOnly? From, DateOnly? To, string? Compare = null)
    : IRequest<Result<EmployeeDeliveryResponse>>;
