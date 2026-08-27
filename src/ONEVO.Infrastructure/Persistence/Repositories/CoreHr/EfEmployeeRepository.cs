using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

public class EfEmployeeRepository : IEmployeeRepository
{
    private readonly ApplicationDbContext _db;
    private readonly IAttendanceReadRepository? _attendance;
    private readonly ILeaveRequestReadRepository? _leaveRequests;

    public EfEmployeeRepository(
        ApplicationDbContext db,
        IAttendanceReadRepository? attendance = null,
        ILeaveRequestReadRepository? leaveRequests = null)
    {
        _db = db;
        _attendance = attendance;
        _leaveRequests = leaveRequests;
    }

    /// <summary>
    /// Proven by EmployeesListIntegrationTests against real PostgreSQL (the EF InMemory
    /// provider used by the unit tests is more lenient and does not reproduce this): EF Core's
    /// query translator treats an anonymous-type projection as transparent and will keep
    /// composing further Where/OrderBy/Select over it, but refuses to translate anything
    /// chained after a projection into a user-defined record (constructor-call projection) -
    /// and a C# tuple literal can't be used in a query expression at all (CS8143: expression
    /// trees may not contain a tuple literal), so a tuple-typed helper method isn't an option
    /// either. Net effect: this whole join-filter-order-project pipeline has to stay inside a
    /// single unbroken `var`-typed anonymous-type chain, with no extracted method boundary in
    /// between (a private method can't declare an anonymous type as its return type). That
    /// forces the join clauses to be duplicated between ListVisibleAsync and
    /// GetVisibleByIdAsync below rather than shared.
    /// </summary>
    public async Task<(IReadOnlyList<EmployeeListItemResponse> Items, int TotalCount)> ListVisibleAsync(
        Guid tenantId,
        EmployeeVisibilityScope scope,
        EmployeeListFilter filter,
                int page,
        int pageSize,
        CancellationToken ct = default,
        EmployeeListAttendanceOptions? attendanceOptions = null)
    {
        var activePrimaryAssignments = _db.PositionAssignments.AsNoTracking()
            .Where(pa => pa.TenantId == tenantId
                && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && pa.AssignmentStatus == PositionAssignmentStatus.Active);

        var directManagerClosure = _db.EmployeeHierarchyClosures.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Depth == 1);

        var joined =
            from e in _db.Employees.AsNoTracking()
            where e.TenantId == tenantId
            join dept in _db.Departments.AsNoTracking() on e.DepartmentId equals dept.Id into deptJoin
            from dept in deptJoin.DefaultIfEmpty()
            join legalEntity in _db.LegalEntities.AsNoTracking() on e.LegalEntityId equals legalEntity.Id into leJoin
            from legalEntity in leJoin.DefaultIfEmpty()
            join empType in _db.EmploymentTypes.AsNoTracking() on e.EmploymentTypeId equals empType.Id into typeJoin
            from empType in typeJoin.DefaultIfEmpty()
            join empStatus in _db.EmploymentStatuses.AsNoTracking() on e.EmploymentStatusId equals empStatus.Id into statusJoin
            from empStatus in statusJoin.DefaultIfEmpty()
            join primaryAssignment in activePrimaryAssignments on e.Id equals primaryAssignment.EmployeeId into paJoin
            from primaryAssignment in paJoin.DefaultIfEmpty()
            join position in _db.Positions.AsNoTracking() on primaryAssignment!.PositionId equals position.Id into posJoin
            from position in posJoin.DefaultIfEmpty()
            join closure in directManagerClosure on e.Id equals closure.DescendantEmployeeId into closureJoin
            from closure in closureJoin.DefaultIfEmpty()
            join manager in _db.Employees.AsNoTracking() on closure!.AncestorEmployeeId equals manager.Id into managerJoin
            from manager in managerJoin.DefaultIfEmpty()
            select new { e, dept, legalEntity, empType, empStatus, position, manager };

        if (filter.RestrictToEmployeeIds is not null)
        {
            // Authoritative visible-id set from IEmployeeAuthorityResolver.ResolveVisibilityAsync -
            // takes precedence over the legacy scope filter below (an empty set is a valid,
            // deliberate "nothing visible" result, not "unrestricted").
            var restrictToEmployeeIds = filter.RestrictToEmployeeIds;
            joined = joined.Where(row => restrictToEmployeeIds.Contains(row.e.Id));
        }
        else if (!scope.CanViewAllTenantEmployees)
        {
            var ownEmployeeId = scope.OwnEmployeeId;
            var coveredPositionIds = scope.CoveredPositionIds;
            var coveredDepartmentIds = scope.CoveredDepartmentIds;
            var companyWideLegalEntityIds = scope.CompanyWideLegalEntityIds;

            joined = joined.Where(row =>
                (ownEmployeeId != null && row.e.Id == ownEmployeeId.Value)
                || (row.position != null && coveredPositionIds.Contains(row.position.Id))
                || (row.dept != null && coveredDepartmentIds.Contains(row.dept.Id))
                || (row.legalEntity != null && companyWideLegalEntityIds.Contains(row.legalEntity.Id)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var normalized = filter.Search.Trim().ToLower();
            joined = joined.Where(row =>
                row.e.FirstName.ToLower().Contains(normalized)
                || row.e.LastName.ToLower().Contains(normalized)
                || row.e.Email.ToLower().Contains(normalized)
                || row.e.EmployeeNumber.ToLower().Contains(normalized));
        }

        if (filter.DepartmentId is not null)
        {
            joined = joined.Where(row => row.dept != null && row.dept.Id == filter.DepartmentId.Value);
        }

        if (filter.LegalEntityId is not null)
        {
            joined = joined.Where(row => row.legalEntity != null && row.legalEntity.Id == filter.LegalEntityId.Value);
        }

        var totalCount = await joined.CountAsync(ct);

        if (attendanceOptions is not null)
        {
            // Attendance-sensitive ordering must happen over the complete filtered result before
            // Skip/Take. All attendance, break, and approved-leave reads are batched; there is
            // intentionally no per-employee Today-state call.
            var rows = await joined.ToListAsync(ct);
            var scheduleByEmployeeId = rows
                .GroupBy(row => row.e.Id)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().legalEntity is { } entity
                        ? AttendanceScheduleResolver.Resolve(entity, attendanceOptions.UtcNow)
                        : null);

            var resolutions = scheduleByEmployeeId.Values
                .Where(resolution => resolution is not null)
                .Select(resolution => resolution!)
                .ToList();
            var employeeIds = scheduleByEmployeeId.Keys.ToArray();
            var attendanceRecords = new List<AttendanceRecord>();
            var approvedLeaveRequests = new List<ONEVO.Domain.Features.Leave.Request.Entities.LeaveRequest>();
            var breakRecords = new List<BreakRecord>();
            if (resolutions.Count != 0)
            {
                var minWorkDate = resolutions.Min(resolution => resolution.WorkDate);
                var maxWorkDate = resolutions.Max(resolution => resolution.WorkDate);
                attendanceRecords = await _db.AttendanceRecords.AsNoTracking()
                    .Where(record => record.TenantId == tenantId
                        && employeeIds.Contains(record.EmployeeId)
                        && record.Date >= minWorkDate
                        && record.Date <= maxWorkDate)
                    .ToListAsync(ct);
                approvedLeaveRequests = _leaveRequests is not null
                    ? (await _leaveRequests.ListApprovedCoveringAsync(
                        tenantId, employeeIds, minWorkDate, maxWorkDate, ct)).ToList()
                    : await _db.LeaveRequests.AsNoTracking()
                        .Where(request => request.TenantId == tenantId
                            && employeeIds.Contains(request.EmployeeId)
                            && request.Status == ONEVO.Domain.Features.Leave.Common.LeaveRequestStatuses.Approved
                            && request.StartDate <= maxWorkDate
                            && request.EndDate >= minWorkDate)
                        .ToListAsync(ct);

                var localWindows = resolutions
                    .Select(resolution => AttendanceTodayStateService.GetLocalDayWindow(
                        resolution.WorkDate, resolution.TimeZone))
                    .ToList();
                var minWindowStart = localWindows.Min(window => window.Start);
                var maxWindowEnd = localWindows.Max(window => window.End);
                breakRecords = _attendance is not null
                    ? (await _attendance.ListBreaksForEmployeesAsync(
                        tenantId, employeeIds, minWindowStart, maxWindowEnd, ct)).ToList()
                    : await _db.BreakRecords.AsNoTracking()
                        .Where(record => record.TenantId == tenantId
                            && employeeIds.Contains(record.EmployeeId)
                            && record.BreakStart < maxWindowEnd
                            && (record.BreakEnd == null || record.BreakEnd > minWindowStart))
                        .ToListAsync(ct);
            }
            var attendanceByEmployeeAndDate = attendanceRecords
                .GroupBy(record => (record.EmployeeId, record.Date))
                .ToDictionary(group => group.Key, group => group.First());
            var leavesByEmployee = approvedLeaveRequests
                .GroupBy(request => request.EmployeeId)
                .ToDictionary(group => group.Key, group => group.ToArray());
            var breaksByEmployee = breakRecords
                .GroupBy(record => record.EmployeeId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<BreakRecord>)group.ToArray());

            var orderedRows = rows
                .Select(row =>
                {
                    scheduleByEmployeeId.TryGetValue(row.e.Id, out var resolution);
                    var record = resolution is not null
                        && attendanceByEmployeeAndDate.TryGetValue((row.e.Id, resolution.WorkDate), out var matchingRecord)
                            ? matchingRecord
                            : null;
                    var hasClockedInToday = record?.ActualStart is not null;
                    var isActive = string.Equals(
                        row.empStatus?.Code, "active", StringComparison.OrdinalIgnoreCase);
                    var schedule = resolution?.Schedule ?? new AttendanceSchedule("not_configured", false, null, null, null);
                    var hasApprovedLeave = resolution is not null
                        && leavesByEmployee.TryGetValue(row.e.Id, out var employeeLeaves)
                        && employeeLeaves.Any(request => request.StartDate <= resolution.WorkDate
                            && request.EndDate >= resolution.WorkDate);
                    breaksByEmployee.TryGetValue(row.e.Id, out var employeeBreaks);
                    var breakUsedMinutes = resolution is not null && employeeBreaks is not null
                        ? AttendanceTodayStateService.CalculateBreakUsage(
                            employeeBreaks,
                            AttendanceTodayStateService.GetLocalDayWindow(
                                resolution.WorkDate, resolution.TimeZone),
                            resolution.LocalNow)
                        : 0;
                    var hasOpenBreak = employeeBreaks?.Any(breakRecord => breakRecord.BreakEnd is null) ?? false;
                    var status = resolution is null
                        ? null
                        : AttendanceDayStatusResolver.Resolve(
                            schedule,
                            "configured",
                            record,
                            hasApprovedLeave,
                            hasOpenBreak,
                            row.legalEntity?.BreakDurationMinutes,
                            breakUsedMinutes,
                            resolution.LocalNow);
                    var attendanceSummary = resolution is null || !isActive
                        ? null
                        : new EmployeeListAttendanceSummaryResponse(
                            status!.AttentionType == "not_clocked_in",
                            status.ShouldHaveClockedIn,
                            hasClockedInToday,
                            resolution.WorkDate,
                            resolution.Timezone,
                            status.ShouldHaveClockedIn ? resolution.Schedule.Start?.ToString("HH:mm") : null,
                            status.AttentionType == "not_clocked_in" ? status.AttentionLabel : null,
                            status.Status,
                            status.StatusLabel,
                            status.AttentionType,
                            status.AttentionSeverity,
                            status.AttentionLabel,
                            breakUsedMinutes,
                            row.legalEntity?.BreakDurationMinutes,
                            status.BreakOverageMinutes,
                            status.IsOverBreakAllowance);

                    return new
                    {
                        Row = row,
                        AttendanceSummary = attendanceSummary,
                    };
                })
                .OrderByDescending(row => GetAttentionPriority(row.AttendanceSummary?.AttentionType))
                .ThenBy(row => row.Row.e.LastName)
                .ThenBy(row => row.Row.e.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(row => new EmployeeListItemResponse(
                    row.Row.e.Id,
                    row.Row.e.EmployeeNumber,
                    row.Row.e.FirstName + " " + row.Row.e.LastName,
                    row.Row.e.Email,
                    row.Row.dept != null ? row.Row.dept.Id : (Guid?)null,
                    row.Row.dept != null ? row.Row.dept.Name : null,
                    row.Row.position != null ? row.Row.position.Id : (Guid?)null,
                    row.Row.position != null ? row.Row.position.Name : null,
                    row.Row.legalEntity != null ? row.Row.legalEntity.Id : (Guid?)null,
                    row.Row.legalEntity != null ? row.Row.legalEntity.Name : null,
                    row.Row.empType != null ? row.Row.empType.Label : row.Row.e.EmploymentTypeId.ToString(),
                    row.Row.empStatus != null ? row.Row.empStatus.Code : "active",
                    row.Row.manager != null ? row.Row.manager.Id : (Guid?)null,
                    row.Row.manager != null ? row.Row.manager.FirstName + " " + row.Row.manager.LastName : null,
                    null,
                    null,
                    row.AttendanceSummary))
                .ToList();

            return (orderedRows, totalCount);
        }

        var items = await joined
            .OrderBy(row => row.e.LastName).ThenBy(row => row.e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new EmployeeListItemResponse(
                row.e.Id,
                row.e.EmployeeNumber,
                row.e.FirstName + " " + row.e.LastName,
                row.e.Email,
                row.dept != null ? row.dept.Id : (Guid?)null,
                row.dept != null ? row.dept.Name : null,
                row.position != null ? row.position.Id : (Guid?)null,
                row.position != null ? row.position.Name : null,
                row.legalEntity != null ? row.legalEntity.Id : (Guid?)null,
                row.legalEntity != null ? row.legalEntity.Name : null,
                row.empType != null ? row.empType.Label : row.e.EmploymentTypeId.ToString(),
                row.empStatus != null ? row.empStatus.Code : "active",
                row.manager != null ? row.manager.Id : (Guid?)null,
                row.manager != null ? row.manager.FirstName + " " + row.manager.LastName : null))
            .ToListAsync(ct);

                return (items, totalCount);
    }

    private static int GetAttentionPriority(string? attentionType)
        => attentionType switch
        {
            "not_clocked_in" => 4,
            "over_break" => 3,
            "worked_during_time_off" => 2,
            "worked_on_non_working_day" => 1,
            _ => 0
        };

    public async Task<IReadOnlyList<EmployeeListItemResponse>> ListInvitedPendingByInviterAsync(

        Guid tenantId, Guid inviterUserId, CancellationToken ct = default)
    {
        var activePrimaryAssignments = _db.PositionAssignments.AsNoTracking()
            .Where(pa => pa.TenantId == tenantId
                && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && pa.AssignmentStatus == PositionAssignmentStatus.Active);

        var joined =
            from token in _db.InvitationTokens.AsNoTracking()
            where token.TenantId == tenantId && token.CreatedById == inviterUserId
                && token.EmployeeId != null && token.UsedAt == null && token.RevokedAt == null
            join e in _db.Employees.AsNoTracking() on token.EmployeeId equals e.Id
            join dept in _db.Departments.AsNoTracking() on e.DepartmentId equals dept.Id into deptJoin
            from dept in deptJoin.DefaultIfEmpty()
            join legalEntity in _db.LegalEntities.AsNoTracking() on e.LegalEntityId equals legalEntity.Id into leJoin
            from legalEntity in leJoin.DefaultIfEmpty()
            join empType in _db.EmploymentTypes.AsNoTracking() on e.EmploymentTypeId equals empType.Id into typeJoin
            from empType in typeJoin.DefaultIfEmpty()
            join empStatus in _db.EmploymentStatuses.AsNoTracking() on e.EmploymentStatusId equals empStatus.Id into statusJoin
            from empStatus in statusJoin.DefaultIfEmpty()
            join primaryAssignment in activePrimaryAssignments on e.Id equals primaryAssignment.EmployeeId into paJoin
            from primaryAssignment in paJoin.DefaultIfEmpty()
            join position in _db.Positions.AsNoTracking() on primaryAssignment!.PositionId equals position.Id into posJoin
            from position in posJoin.DefaultIfEmpty()
            select new { e, dept, legalEntity, empType, empStatus, position, token.Status, token.ExpiresAt };

        return await joined
            .OrderBy(row => row.e.LastName).ThenBy(row => row.e.Id)
            .Select(row => new EmployeeListItemResponse(
                row.e.Id,
                row.e.EmployeeNumber,
                row.e.FirstName + " " + row.e.LastName,
                row.e.Email,
                row.dept != null ? row.dept.Id : (Guid?)null,
                row.dept != null ? row.dept.Name : null,
                row.position != null ? row.position.Id : (Guid?)null,
                row.position != null ? row.position.Name : null,
                row.legalEntity != null ? row.legalEntity.Id : (Guid?)null,
                row.legalEntity != null ? row.legalEntity.Name : null,
                row.empType != null ? row.empType.Label : row.e.EmploymentTypeId.ToString(),
                row.empStatus != null ? row.empStatus.Code : "active",
                null,
                null,
                row.Status,
                row.ExpiresAt))
            .ToListAsync(ct);
    }

    public async Task<EmployeeListItemResponse?> GetVisibleByIdAsync(
        Guid tenantId, EmployeeVisibilityScope scope, Guid employeeId, CancellationToken ct = default)
    {
        var activePrimaryAssignments = _db.PositionAssignments.AsNoTracking()
            .Where(pa => pa.TenantId == tenantId
                && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && pa.AssignmentStatus == PositionAssignmentStatus.Active);

        var directManagerClosure = _db.EmployeeHierarchyClosures.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Depth == 1);

        var joined =
            from e in _db.Employees.AsNoTracking()
            where e.TenantId == tenantId && e.Id == employeeId
            join dept in _db.Departments.AsNoTracking() on e.DepartmentId equals dept.Id into deptJoin
            from dept in deptJoin.DefaultIfEmpty()
            join legalEntity in _db.LegalEntities.AsNoTracking() on e.LegalEntityId equals legalEntity.Id into leJoin
            from legalEntity in leJoin.DefaultIfEmpty()
            join empType in _db.EmploymentTypes.AsNoTracking() on e.EmploymentTypeId equals empType.Id into typeJoin
            from empType in typeJoin.DefaultIfEmpty()
            join empStatus in _db.EmploymentStatuses.AsNoTracking() on e.EmploymentStatusId equals empStatus.Id into statusJoin
            from empStatus in statusJoin.DefaultIfEmpty()
            join primaryAssignment in activePrimaryAssignments on e.Id equals primaryAssignment.EmployeeId into paJoin
            from primaryAssignment in paJoin.DefaultIfEmpty()
            join position in _db.Positions.AsNoTracking() on primaryAssignment!.PositionId equals position.Id into posJoin
            from position in posJoin.DefaultIfEmpty()
            join closure in directManagerClosure on e.Id equals closure.DescendantEmployeeId into closureJoin
            from closure in closureJoin.DefaultIfEmpty()
            join manager in _db.Employees.AsNoTracking() on closure!.AncestorEmployeeId equals manager.Id into managerJoin
            from manager in managerJoin.DefaultIfEmpty()
            select new { e, dept, legalEntity, empType, empStatus, position, manager };

        if (!scope.CanViewAllTenantEmployees)
        {
            var ownEmployeeId = scope.OwnEmployeeId;
            var coveredPositionIds = scope.CoveredPositionIds;
            var coveredDepartmentIds = scope.CoveredDepartmentIds;
            var companyWideLegalEntityIds = scope.CompanyWideLegalEntityIds;

            joined = joined.Where(row =>
                (ownEmployeeId != null && row.e.Id == ownEmployeeId.Value)
                || (row.position != null && coveredPositionIds.Contains(row.position.Id))
                || (row.dept != null && coveredDepartmentIds.Contains(row.dept.Id))
                || (row.legalEntity != null && companyWideLegalEntityIds.Contains(row.legalEntity.Id)));
        }

        return await joined
            .Select(row => new EmployeeListItemResponse(
                row.e.Id,
                row.e.EmployeeNumber,
                row.e.FirstName + " " + row.e.LastName,
                row.e.Email,
                row.dept != null ? row.dept.Id : (Guid?)null,
                row.dept != null ? row.dept.Name : null,
                row.position != null ? row.position.Id : (Guid?)null,
                row.position != null ? row.position.Name : null,
                row.legalEntity != null ? row.legalEntity.Id : (Guid?)null,
                row.legalEntity != null ? row.legalEntity.Name : null,
                row.empType != null ? row.empType.Label : row.e.EmploymentTypeId.ToString(),
                row.empStatus != null ? row.empStatus.Code : "active",
                row.manager != null ? row.manager.Id : (Guid?)null,
                row.manager != null ? row.manager.FirstName + " " + row.manager.LastName : null))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<EmployeeEntity?> GetByIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == employeeId, ct);

    public async Task<EmployeeEntity?> GetDefaultForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.UserId == userId)
            .ToListAsync(ct);

        if (employees.Count <= 1)
            return employees.FirstOrDefault();

        var employeeIds = employees.Select(e => e.Id).ToList();
        var latestEmployeeId = await _db.PositionAssignments.AsNoTracking()
            .Where(pa => pa.TenantId == tenantId
                && employeeIds.Contains(pa.EmployeeId)
                && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && pa.AssignmentStatus == PositionAssignmentStatus.Active)
            .OrderByDescending(pa => pa.EffectiveFrom)
            .Select(pa => pa.EmployeeId)
            .FirstOrDefaultAsync(ct);

        if (latestEmployeeId != Guid.Empty)
            return employees.FirstOrDefault(e => e.Id == latestEmployeeId);

        return employees[0];
    }

    public async Task<EmployeeEntity?> GetByUserAndLegalEntityAsync(
        Guid tenantId, Guid userId, Guid legalEntityId, CancellationToken ct = default)
        => await (
            from employee in _db.Employees.AsNoTracking()
            join status in _db.EmploymentStatuses.AsNoTracking()
                on employee.EmploymentStatusId equals status.Id
            where employee.TenantId == tenantId
                && employee.UserId == userId
                && employee.LegalEntityId == legalEntityId
                && status.Code == "active"
            select employee)
            .FirstOrDefaultAsync(ct);

    public async Task<EmployeeEntity?> GetTrackedByIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => await _db.Employees.FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == employeeId, ct);

    public async Task<uint?> GetVersionTokenAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Id == employeeId)
            .Select(e => EF.Property<uint?>(e, "xmin"))
            .FirstOrDefaultAsync(ct);

    public void SetExpectedVersion(EmployeeEntity employee, string expectedVersion)
    {
        if (!uint.TryParse(expectedVersion, out var expectedXmin))
        {
            return;
        }

        _db.Entry(employee).Property("xmin").OriginalValue = expectedXmin;
    }

    public async Task<bool> EmailExistsAsync(Guid tenantId, string email, Guid? excludeId, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLower();
        var query = _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Email.ToLower() == normalized);

        if (excludeId is not null)
        {
            query = query.Where(e => e.Id != excludeId.Value);
        }

        return await query.AnyAsync(ct);
    }

    public async Task<bool> EmployeeExistsInLegalEntityAsync(
        Guid tenantId, Guid legalEntityId, string email, Guid? excludeId, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLower();
        var query = _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                && e.LegalEntityId == legalEntityId
                && e.Email.ToLower() == normalized);

        if (excludeId is not null)
        {
            query = query.Where(e => e.Id != excludeId.Value);
        }

        return await query.AnyAsync(ct);
    }

    public async Task<bool> EmployeeNumberExistsAsync(Guid tenantId, string employeeNumber, Guid? excludeId, CancellationToken ct = default)
    {
        // Ignore soft-delete filter: the unique index is tenant+employee_number with no
        // IsDeleted filter, so archived rows still occupy the number.
        var query = _db.Employees.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.EmployeeNumber == employeeNumber);

        if (excludeId is not null)
        {
            query = query.Where(e => e.Id != excludeId.Value);
        }

        return await query.AnyAsync(ct);
    }

    public async Task<int> GetNextEmployeeNumberSequenceAsync(Guid tenantId, string prefix, CancellationToken ct = default)
    {
        var expectedPrefix = prefix + "-";
        var numbers = await _db.Employees.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.EmployeeNumber.StartsWith(expectedPrefix))
            .Select(e => e.EmployeeNumber)
            .ToListAsync(ct);

        var maxSequence = 0;
        foreach (var number in numbers)
        {
            var suffix = number.AsSpan(expectedPrefix.Length);
            if (suffix.Length > 0 && int.TryParse(suffix, out var sequence) && sequence > maxSequence)
                maxSequence = sequence;
        }

        return maxSequence + 1;
    }

    public async Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default)
        => await _db.Employees.AsNoTracking().CountAsync(e => e.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<Guid>> ListActiveEmployeeIdsAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid>? departmentIds, CancellationToken ct = default)
    {
        if (departmentIds is not null && departmentIds.Count == 0)
            return Array.Empty<Guid>();

        var query =
            from e in _db.Employees.AsNoTracking()
            join status in _db.EmploymentStatuses.AsNoTracking() on e.EmploymentStatusId equals status.Id
            where e.TenantId == tenantId && e.LegalEntityId == legalEntityId && status.Code == "active"
            select e;

        if (departmentIds is not null)
        {
            query = query.Where(e => e.DepartmentId != null && departmentIds.Contains(e.DepartmentId.Value));
        }

        return await query.Select(e => e.Id).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> ListActiveEmployeeIdsByIdsAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return Array.Empty<Guid>();

        return await (
            from e in _db.Employees.AsNoTracking()
            join status in _db.EmploymentStatuses.AsNoTracking() on e.EmploymentStatusId equals status.Id
            where e.TenantId == tenantId
                && e.LegalEntityId == legalEntityId
                && status.Code == "active"
                && employeeIds.Contains(e.Id)
            select e.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, EmployeeEntity>> ListByIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return new Dictionary<Guid, EmployeeEntity>();

        var rows = await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && employeeIds.Contains(e.Id))
            .ToListAsync(ct);
        return rows.ToDictionary(e => e.Id);
    }

    public async Task AddAsync(EmployeeEntity employee, CancellationToken ct = default)
        => await _db.Employees.AddAsync(employee, ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
