using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.Meetings.Queries.GetMeetingSignals;

public class GetMeetingSignalsQueryHandler
    : IRequestHandler<GetMeetingSignalsQuery, Result<PagedResult<MeetingSignalDto>>>
{
    private readonly IMeetingSignalRepository _signals;
    private readonly ITenantContext _tenantContext;

    public GetMeetingSignalsQueryHandler(IMeetingSignalRepository signals, ITenantContext tenantContext)
    {
        _signals = signals;
        _tenantContext = tenantContext;
    }

    public async Task<Result<PagedResult<MeetingSignalDto>>> Handle(
        GetMeetingSignalsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Result<PagedResult<MeetingSignalDto>>.Failure("Tenant context is required.", 401);

        if (request.EmployeeId == Guid.Empty)
            return Result<PagedResult<MeetingSignalDto>>.Failure("employeeId is required.", 400);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 500 ? 100 : request.PageSize;
        var tenantId = _tenantContext.TenantId;

        var total = await _signals.GetTotalCountAsync(tenantId, request.EmployeeId, request.Date, ct);
        var items = await _signals.GetByEmployeeDateAsync(tenantId, request.EmployeeId, request.Date, page, pageSize, ct);

        var dtos = items.Select(s => new MeetingSignalDto
        {
            Id = s.Id,
            CapturedAt = s.CapturedAt,
            IsMeetingAppRunning = s.IsMeetingAppRunning,
            ProcessName = s.ProcessName
        }).ToList();

        return Result<PagedResult<MeetingSignalDto>>.Success(
            new PagedResult<MeetingSignalDto>(dtos, page, pageSize, total));
    }
}
