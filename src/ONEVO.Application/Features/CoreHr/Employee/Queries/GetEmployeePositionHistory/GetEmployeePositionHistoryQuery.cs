using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeePositionHistory;

public sealed record PositionHistoryEntryResponse(
    string PositionName, string? DepartmentName, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    string? ChangeReason, string InitiatedByName, string? ApprovedByName);

public sealed record GetEmployeePositionHistoryQuery(Guid EmployeeId)
    : IRequest<Result<IReadOnlyList<PositionHistoryEntryResponse>>>;
