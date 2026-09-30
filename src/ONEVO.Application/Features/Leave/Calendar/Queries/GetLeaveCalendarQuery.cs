using MediatR;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.Leave.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Leave.Calendar.Helpers;
using ONEVO.Application.Features.Leave.Calendar.Mappers;
using ONEVO.Application.Features.Leave.Calendar.Options;
using ONEVO.Application.Features.Leave.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Calendar.Services;

namespace ONEVO.Application.Features.Leave.Calendar.Queries;

public sealed record GetLeaveCalendarQuery(
    int Year,
    int Month,
    Guid? DepartmentId,
    bool? IncludeTentative)
    : IRequest<Result<LeaveCalendarMonthResponse>>;

public sealed class GetLeaveCalendarQueryHandler
    : IRequestHandler<GetLeaveCalendarQuery, Result<LeaveCalendarMonthResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ILeaveVisibilityScopeProvider _leaveScopes;
    private readonly ILeaveCalendarRepository _repository;
    private readonly ILeaveCalendarHolidayProvider _holidays;
    private readonly LeaveCalendarRequestProjector _projector;
    private readonly LeaveCalendarOptions _options;

    public GetLeaveCalendarQueryHandler(
        ICurrentUser currentUser,
        ILeaveVisibilityScopeProvider leaveScopes,
        ILeaveCalendarRepository repository,
        ILeaveCalendarHolidayProvider holidays,
        LeaveCalendarRequestProjector projector,
        IOptions<LeaveCalendarOptions> options)
    {
        _currentUser = currentUser;
        _leaveScopes = leaveScopes;
        _repository = repository;
        _holidays = holidays;
        _projector = projector;
        _options = options.Value;
    }

    public async Task<Result<LeaveCalendarMonthResponse>> Handle(
        GetLeaveCalendarQuery query,
        CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveCalendarMonthResponse>.Forbidden(LeaveCalendarMessages.AuthRequired);

        if (_currentUser.TenantId == Guid.Empty)
            return Result<LeaveCalendarMonthResponse>.Forbidden(LeaveCalendarMessages.TenantMissing);

        if (!_currentUser.HasPermission("calendar:read"))
            return Result<LeaveCalendarMonthResponse>.Forbidden(LeaveCalendarMessages.CalendarPermissionRequired);

        var rangeResult = LeaveCalendarMonthRange.From(query.Year, query.Month);
        if (!rangeResult.IsSuccess)
            return Result<LeaveCalendarMonthResponse>.Failure(rangeResult.Error!, rangeResult.StatusCode ?? 400);

        var scopeResult = await ResolveScopeAsync(ct);
        if (!scopeResult.IsSuccess)
            return Result<LeaveCalendarMonthResponse>.Failure(scopeResult.Error!, scopeResult.StatusCode ?? 403);

        var includeTentative = query.IncludeTentative ?? _options.DefaultIncludeTentativeBlocks;
        var range = rangeResult.Value!;
        var rows = await _repository.ListMonthRequestsAsync(
            _currentUser.TenantId,
            scopeResult.Value!,
            new LeaveCalendarRequestFilter(range.MonthStart, range.MonthEnd, query.DepartmentId, includeTentative),
            ct);

        var legalEntityIds = rows
            .Select(row => row.LegalEntityId)
            .OfType<Guid>()
            .Distinct()
            .ToArray();

        var holidays = await _holidays.ListHolidaysAsync(
            _currentUser.TenantId,
            legalEntityIds,
            range.MonthStart,
            range.MonthEnd,
            ct);

        var instances = _projector.Project(rows, range.MonthStart, range.MonthEnd, includeTentative);
        var response = LeaveCalendarMapper.ToMonthResponse(range, includeTentative, instances, holidays, _options);
        return Result<LeaveCalendarMonthResponse>.Success(response);
    }

    private async Task<Result<EmployeeVisibilityScope>> ResolveScopeAsync(CancellationToken ct)
    {
        var resolution = await _leaveScopes.ResolveForCurrentUserAsync(ct);
        return resolution.Failure switch
        {
            LeaveVisibilityScopeFailure.NoEmployee =>
                Result<EmployeeVisibilityScope>.NotFound(LeaveCalendarMessages.NoEmployee),
            LeaveVisibilityScopeFailure.NoLeaveReadPermission =>
                Result<EmployeeVisibilityScope>.Forbidden(LeaveCalendarMessages.LeaveScopeRequired),
            _ => Result<EmployeeVisibilityScope>.Success(resolution.Scope!),
        };
    }
}
