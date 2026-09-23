using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Balance.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Balance.Queries.ListTeamBalances;

public record ListTeamBalancesQuery(
    int Year,
    Guid? DepartmentId,
    Guid? LeaveTypeId,
    string? Search) : IRequest<Result<IReadOnlyList<LeaveBalanceResponse>>>;
