using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Request.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Request.Queries.ListMyLeaveRequests;

public sealed record ListMyLeaveRequestsQuery(
    string? Status,
    DateOnly? FromDate,
    DateOnly? ToDate,
    Guid? LeaveTypeId) : IRequest<Result<IReadOnlyList<LeaveRequestListItemResponse>>>;
