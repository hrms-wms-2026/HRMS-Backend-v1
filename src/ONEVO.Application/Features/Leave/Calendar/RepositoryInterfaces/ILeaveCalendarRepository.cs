using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Calendar.RepositoryInterfaces;

public interface ILeaveCalendarRepository
{
    Task<IReadOnlyList<LeaveCalendarRequestRow>> ListMonthRequestsAsync(
        Guid tenantId,
        EmployeeVisibilityScope scope,
        LeaveCalendarRequestFilter filter,
        CancellationToken ct = default);
}

public sealed record LeaveCalendarRequestFilter(
    DateOnly MonthStart,
    DateOnly MonthEnd,
    Guid? DepartmentId,
    bool IncludeTentativeBlocks);

public sealed record LeaveCalendarRequestRow(
    LeaveRequest Request,
    string EmployeeName,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LegalEntityId,
    string? LegalEntityName,
    string LeaveTypeName,
    string LeaveTypeCode,
    string LeaveTypeCategory);
