using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Queries.LocationChangeRequests;

public sealed record ListMyLocationChangeRequestsQuery(
    string? Status,
    PagedRequest Paging) : IRequest<Result<PagedResult<LocationChangeRequestResponse>>>;

public sealed record ListLocationChangeRequestApprovalsQuery(
    PagedRequest Paging) : IRequest<Result<PagedResult<LocationChangeRequestResponse>>>;
