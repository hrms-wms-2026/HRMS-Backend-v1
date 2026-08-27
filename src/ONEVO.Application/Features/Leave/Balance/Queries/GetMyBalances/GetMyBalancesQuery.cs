using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Balance.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Balance.Queries.GetMyBalances;

public record GetMyBalancesQuery(int Year) : IRequest<Result<IReadOnlyList<LeaveBalanceResponse>>>;
