namespace ONEVO.Application.Features.CoreHr.Employee.Models;

/// <summary>In-memory mirror of the EmployeeVisibilityScope SQL filter in
/// EfLeaveCalendarRepository.ListMonthRequestsAsync: own OR covered primary position OR covered
/// department (exact, no descendants) OR company-wide legal entity. Used where the subjects are
/// already loaded, e.g. My Team leave masking (My Team spec §9.3).
/// EmployeeVisibilityScopeMatcherParityTests keeps it identical to the SQL.</summary>
public static class EmployeeVisibilityScopeMatcher
{
    public static bool Includes(
        EmployeeVisibilityScope scope, Guid employeeId, Guid? primaryPositionId, Guid? departmentId, Guid? legalEntityId)
    {
        if (scope.CanViewAllTenantEmployees)
            return true;
        if (scope.OwnEmployeeId is Guid own && own == employeeId)
            return true;
        if (primaryPositionId is Guid position && scope.CoveredPositionIds.Contains(position))
            return true;
        if (departmentId is Guid department && scope.CoveredDepartmentIds.Contains(department))
            return true;
        return legalEntityId is Guid le && scope.CompanyWideLegalEntityIds.Contains(le);
    }
}
