using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Queries.DeviceChangeRequests;

public sealed record ListDeviceChangeRequestApprovalsQuery(
    PagedRequest Paging) : IRequest<Result<PagedResult<DeviceChangeRequestResponse>>>;
