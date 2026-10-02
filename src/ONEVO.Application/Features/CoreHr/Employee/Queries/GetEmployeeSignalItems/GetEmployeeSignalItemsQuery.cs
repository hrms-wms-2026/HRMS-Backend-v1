using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeSignalItems;

public sealed record GetEmployeeSignalItemsQuery(Guid EmployeeId, string Key, DateOnly? From, DateOnly? To)
    : IRequest<Result<EmployeeSignalItemsResponse>>;
