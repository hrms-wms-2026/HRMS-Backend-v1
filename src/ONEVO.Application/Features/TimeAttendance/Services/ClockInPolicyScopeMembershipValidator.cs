using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Models;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Services;

public interface IClockInPolicyScopeMembershipValidator
{
    Task<Result?> ValidateAsync(
        Guid tenantId,
        Guid legalEntityId,
        ClockInPolicyScopeInput scope,
        CancellationToken ct = default);
}

public sealed class ClockInPolicyScopeMembershipValidator : IClockInPolicyScopeMembershipValidator
{
    private readonly IDepartmentRepository _departments;
    private readonly IPositionRepository _positions;
    private readonly IEmployeeRepository _employees;

    public ClockInPolicyScopeMembershipValidator(
        IDepartmentRepository departments,
        IPositionRepository positions,
        IEmployeeRepository employees)
    {
        _departments = departments;
        _positions = positions;
        _employees = employees;
    }

    public async Task<Result?> ValidateAsync(
        Guid tenantId,
        Guid legalEntityId,
        ClockInPolicyScopeInput scope,
        CancellationToken ct = default)
    {
        if (scope.Type == ClockInPolicy.ScopeDepartment)
        {
            foreach (var departmentId in scope.DepartmentIds ?? Array.Empty<Guid>())
            {
                var exists = await _departments.ExistsAsync(tenantId, legalEntityId, departmentId, ct);
                if (!exists)
                    return Result.NotFound($"Department '{departmentId}' was not found in this legal entity.");
            }
        }

        if (scope.Type == ClockInPolicy.ScopePosition)
        {
            foreach (var positionId in scope.PositionIds ?? Array.Empty<Guid>())
            {
                var position = await _positions.GetByIdForLegalEntityAsync(
                    tenantId, legalEntityId, positionId, ct);
                if (position is null)
                    return Result.NotFound($"Position '{positionId}' was not found in this legal entity.");
            }
        }

        if (scope.Type == ClockInPolicy.ScopeEmployee)
        {
            foreach (var employeeId in scope.EmployeeIds ?? Array.Empty<Guid>())
            {
                var employee = await _employees.GetByIdAsync(tenantId, employeeId, ct);
                if (employee is null || employee.LegalEntityId != legalEntityId)
                    return Result.NotFound($"Employee '{employeeId}' was not found in this legal entity.");
            }
        }

        return null;
    }
}
