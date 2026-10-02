using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeApprovalActivity;

/// <summary>AllItems=true skips the MaxItems cap (used by the Needs Attention drill-down).</summary>
public sealed record GetEmployeeApprovalActivityQuery(Guid EmployeeId, DateOnly? From, DateOnly? To, bool AllItems = false)
    : IRequest<Result<EmployeeApprovalActivityResponse>>;
