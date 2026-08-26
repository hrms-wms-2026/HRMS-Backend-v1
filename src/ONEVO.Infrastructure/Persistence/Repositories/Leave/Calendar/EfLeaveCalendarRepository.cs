using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.Leave.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Mappers;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Infrastructure.Persistence.Repositories.Leave.Calendar;

public sealed class EfLeaveCalendarRepository : ILeaveCalendarRepository
{
    private readonly ApplicationDbContext _db;

    public EfLeaveCalendarRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<LeaveCalendarRequestRow>> ListMonthRequestsAsync(
        Guid tenantId,
        EmployeeVisibilityScope scope,
        LeaveCalendarRequestFilter filter,
        CancellationToken ct = default)
    {
        var activePrimaryAssignments = _db.PositionAssignments.AsNoTracking()
            .Where(pa => pa.TenantId == tenantId
                && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && pa.AssignmentStatus == PositionAssignmentStatus.Active);

        var query =
            from request in _db.LeaveRequests.AsNoTracking()
            join employee in _db.Employees.AsNoTracking() on request.EmployeeId equals employee.Id
            join leaveType in _db.LeaveTypes.AsNoTracking() on request.LeaveTypeId equals leaveType.Id
            join department in _db.Departments.AsNoTracking() on employee.DepartmentId equals department.Id into departments
            from department in departments.DefaultIfEmpty()
            join legalEntity in _db.LegalEntities.AsNoTracking() on employee.LegalEntityId equals legalEntity.Id into legalEntities
            from legalEntity in legalEntities.DefaultIfEmpty()
            join primaryAssignment in activePrimaryAssignments on employee.Id equals primaryAssignment.EmployeeId into assignmentJoin
            from primaryAssignment in assignmentJoin.DefaultIfEmpty()
            where request.TenantId == tenantId
                && employee.TenantId == tenantId
                && leaveType.TenantId == tenantId
                && request.StartDate <= filter.MonthEnd
                && request.EndDate >= filter.MonthStart
                && (
                    request.Status == LeaveRequestStatuses.Approved
                    || (filter.IncludeTentativeBlocks
                        && (request.Status == LeaveRequestStatuses.Pending
                            || request.Status == LeaveRequestStatuses.InformationRequested))
                    || (request.Status == LeaveRequestStatuses.Cancelled
                        && request.PartialCancelEffectiveDate != null
                        && request.PartialCancelEffectiveDate > filter.MonthStart))
            select new { request, employee, leaveType, department, legalEntity, primaryAssignment };

        if (!scope.CanViewAllTenantEmployees)
        {
            var ownEmployeeId = scope.OwnEmployeeId;
            var coveredPositionIds = scope.CoveredPositionIds;
            var coveredDepartmentIds = scope.CoveredDepartmentIds;
            var companyWideLegalEntityIds = scope.CompanyWideLegalEntityIds;

            query = query.Where(row =>
                (ownEmployeeId != null && row.employee.Id == ownEmployeeId.Value)
                || (row.primaryAssignment != null && coveredPositionIds.Contains(row.primaryAssignment.PositionId))
                || (row.department != null && coveredDepartmentIds.Contains(row.department.Id))
                || (row.legalEntity != null && companyWideLegalEntityIds.Contains(row.legalEntity.Id)));
        }

        if (filter.DepartmentId is { } departmentId)
            query = query.Where(row => row.employee.DepartmentId == departmentId);

        var rows = await query
            .OrderBy(row => row.request.StartDate)
            .ThenBy(row => row.employee.LastName)
            .ThenBy(row => row.employee.FirstName)
            .ToListAsync(ct);

        return rows.Select(row => new LeaveCalendarRequestRow(
            row.request,
            LeaveEntitlementMapper.EmployeeName(row.employee.FirstName, row.employee.LastName),
            row.employee.DepartmentId,
            row.department?.Name,
            row.employee.LegalEntityId,
            row.legalEntity?.Name,
            row.leaveType.Name,
            row.leaveType.Code,
            row.leaveType.Category)).ToList();
    }
}
