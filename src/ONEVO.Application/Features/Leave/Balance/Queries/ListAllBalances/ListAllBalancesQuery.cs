using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Balance.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Balance.Queries.ListAllBalances;

public record ListAllBalancesQuery(
    int Year,
    Guid? LegalEntityId,
    Guid? DepartmentId,
    Guid? LeaveTypeId,
    int? EmploymentStatusId,
    string? Search) : IRequest<Result<IReadOnlyList<LeaveBalanceResponse>>>;
