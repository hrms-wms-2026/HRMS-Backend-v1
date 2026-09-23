using MediatR;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
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
    private readonly IEmployeeRepository _employees;
    private readonly IEmployeeVisibilityScopeResolver _visibilityScopes;
    private readonly ILeaveCalendarRepository _repository;
    private readonly ILeaveCalendarHolidayProvider _holidays;
    private readonly LeaveCalendarRequestProjector _projector;
    private readonly LeaveCalendarOptions _options;

    public GetLeaveCalendarQueryHandler(
        ICurrentUser currentUser,
        IEmployeeRepository employees,
        IEmployeeVisibilityScopeResolver visibilityScopes,
        ILeaveCalendarRepository repository,
        ILeaveCalendarHolidayProvider holidays,
        LeaveCalendarRequestProjector projector,
        IOptions<LeaveCalendarOptions> options)
    {
        _currentUser = currentUser;
        _employees = employees;
        _visibilityScopes = visibilityScopes;
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
        if (_currentUser.HasPermission("leave:manage") || _currentUser.HasPermission("leave:read"))
            return Result<EmployeeVisibilityScope>.Success(EmployeeVisibilityScope.Unrestricted());

        if (_currentUser.HasPermission("leave:read-team"))
        {
            return Result<EmployeeVisibilityScope>.Success(
                await _visibilityScopes.ResolveAsync(_currentUser.TenantId, _currentUser.UserId, ct));
        }

        if (_currentUser.HasPermission("leave:read-own"))
        {
            var employee = await _employees.GetByUserIdAsync(_currentUser.TenantId, _currentUser.UserId, ct);
            if (employee is null)
                return Result<EmployeeVisibilityScope>.NotFound(LeaveCalendarMessages.NoEmployee);

            return Result<EmployeeVisibilityScope>.Success(new EmployeeVisibilityScope(
                false,
                employee.Id,
                new HashSet<Guid>(),
                new HashSet<Guid>(),
                new HashSet<Guid>()));
        }

        return Result<EmployeeVisibilityScope>.Forbidden(LeaveCalendarMessages.LeaveScopeRequired);
    }
}
