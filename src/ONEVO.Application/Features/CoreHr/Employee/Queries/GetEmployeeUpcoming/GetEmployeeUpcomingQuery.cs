using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeUpcoming;

/// <summary>Limit caps how many of the earliest items come back; the response's Total counts them all.</summary>
public sealed record GetEmployeeUpcomingQuery(Guid EmployeeId, int Days = 14, int Limit = GetEmployeeUpcomingQueryHandler.DefaultLimit)
    : IRequest<Result<EmployeeUpcomingResponse>>;
