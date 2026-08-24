using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;

namespace ONEVO.Application.Features.TimeAttendance.Queries;

public sealed class AttendanceReadHandler(
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    IAttendanceReadRepository attendance,
    IEmployeeAuthorityResolver authority,
    IAttendanceTodayStateService todayState)
    : IRequestHandler<GetAttendanceTodayQuery, Result<AttendanceTodayResponse>>,
      IRequestHandler<GetMyAttendanceHistoryQuery, Result<IReadOnlyList<AttendanceHistoryRow>>>,
      IRequestHandler<GetCoveredAttendanceHistoryQuery, Result<IReadOnlyList<AttendanceHistoryRow>>>
{
    private const string AttendanceReadPermission = "attendance:read";

    public Task<Result<AttendanceTodayResponse>> Handle(GetAttendanceTodayQuery _, CancellationToken ct)
        => todayState.GetTodayAsync(ct);

    public async Task<Result<IReadOnlyList<AttendanceHistoryRow>>> Handle(
        GetMyAttendanceHistoryQuery query, CancellationToken ct)
    {
        var validation = ValidateRange(query.From, query.To);
        if (validation is not null)
            return Result<IReadOnlyList<AttendanceHistoryRow>>.Failure(validation);

        var employee = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (employee is null)
            return Result<IReadOnlyList<AttendanceHistoryRow>>.NotFound("Current employee record was not found.");

        var records = await attendance.ListRecordsAsync(
            currentUser.TenantId, [employee.Id], query.From, query.To, ct);
        return Result<IReadOnlyList<AttendanceHistoryRow>>.Success(
            await BuildRowsAsync(records, includeEmployee: false, employee.LegalEntityId, ct));
    }

    public async Task<Result<IReadOnlyList<AttendanceHistoryRow>>> Handle(
        GetCoveredAttendanceHistoryQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(AttendanceReadPermission))
            return Result<IReadOnlyList<AttendanceHistoryRow>>.Forbidden();

        var validation = ValidateRange(query.From, query.To);
        if (validation is not null)
            return Result<IReadOnlyList<AttendanceHistoryRow>>.Failure(validation);

        var actor = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (actor?.LegalEntityId is null)
            return Result<IReadOnlyList<AttendanceHistoryRow>>.NotFound("Current employee record was not found.");

        var visibility = await authority.ResolveVisibilityAsync(
            new EmployeeAuthorityVisibilityRequest(
                currentUser.UserId,
                actor.LegalEntityId.Value,
                AttendanceReadPermission,
                IncludeSelf: true,
                EmployeeAuthorityPurpose.TimeTrackingRead), ct);

        IReadOnlyCollection<Guid> employeeIds;
        if (query.EmployeeId is Guid requestedEmployeeId)
        {
            if (!visibility.EmployeeIds.Contains(requestedEmployeeId))
                return Result<IReadOnlyList<AttendanceHistoryRow>>.Forbidden();

            employeeIds = [requestedEmployeeId];
        }
        else
        {
            employeeIds = visibility.EmployeeIds;
        }

        var records = await attendance.ListRecordsAsync(
            currentUser.TenantId, employeeIds, query.From, query.To, ct);
        return Result<IReadOnlyList<AttendanceHistoryRow>>.Success(
            await BuildRowsAsync(records, includeEmployee: true, actor.LegalEntityId, ct));
    }

    private async Task<IReadOnlyList<AttendanceHistoryRow>> BuildRowsAsync(
        IReadOnlyList<Domain.Features.TimeAttendance.Entities.AttendanceRecord> records,
        bool includeEmployee,
        Guid? legalEntityId,
        CancellationToken ct)
    {
        IReadOnlyDictionary<Guid, AttendanceHistoryEmployee> identities =
            includeEmployee && legalEntityId is Guid entityId
                ? await attendance.ListEmployeeIdentitiesAsync(
                    currentUser.TenantId,
                    entityId,
                    records.Select(x => x.EmployeeId).Distinct().ToArray(),
                    ct)
                : new Dictionary<Guid, AttendanceHistoryEmployee>();

        return records.Select(record => new AttendanceHistoryRow(
            record.Id,
            record.Date,
            includeEmployee && identities.TryGetValue(record.EmployeeId, out var identity) ? identity : null,
            record.ActualStart,
            record.ActualEnd,
            record.ActualStart is not null && record.ActualEnd is null,
            record.BreakMinutes,
            record.WorkedMinutes,
            NormalizeWorkMode(record.ExpectedWorkArea),
            record.AttendanceSource,
            record.Status,
            CanViewDetails: true,
            CanRequestCorrection: false,
            CanRequestWorkAreaChange: false,
            CanCorrect: false)).ToList();
    }

    private static string? NormalizeWorkMode(string? value)
        => string.Equals(value, "either", StringComparison.OrdinalIgnoreCase)
            ? "hybrid"
            : value?.ToLowerInvariant();

    private static string? ValidateRange(DateOnly from, DateOnly to)
        => from > to ? "from must be less than or equal to to." : null;
}
