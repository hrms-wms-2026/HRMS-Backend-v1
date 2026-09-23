using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Queries.WorkAreaChangeRequests;

public sealed record ListMyWorkAreaChangeRequestsQuery(
    DateOnly? From,
    DateOnly? To,
    string? Status,
    PagedRequest Paging) : IRequest<Result<PagedResult<WorkAreaChangeRequestResponse>>>;

public sealed record ListWorkAreaChangeRequestApprovalsQuery(
    DateOnly? From,
    DateOnly? To,
    PagedRequest Paging) : IRequest<Result<PagedResult<WorkAreaChangeRequestResponse>>>;
