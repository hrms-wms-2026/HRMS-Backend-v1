using ONEVO.Application.Features.CoreHr.Employee.Queries.ListEmployees;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Feature-specific guards for EMPLOYEE_LIST_AUTHORITY_RESOLVER_BACKEND_PART1, in addition to the
/// generic layering rules in LayerDependencyTests (which already prove ONEVO.Application does not
/// reference EntityFrameworkCore or any DbContext type) and EmployeeAuthorityResolverArchitectureTests
/// (which covers the resolver itself, built in Part 0).
/// </summary>
public sealed class ListEmployeesAuthorityResolverArchitectureTests
{
    [Fact] // Requirement 15: the handler depends on IEmployeeAuthorityResolver, not a concrete
           // infrastructure type, and no longer depends on the legacy IEmployeeVisibilityScopeResolver.
    public void ListEmployeesQueryHandler_DependsOnIEmployeeAuthorityResolver_ViaConstructor()
    {
        var constructor = typeof(ListEmployeesQueryHandler).GetConstructors().Single();
        var parameterTypes = constructor.GetParameters().Select(p => p.ParameterType).ToList();

        Assert.Contains(typeof(IEmployeeAuthorityResolver), parameterTypes);
        Assert.DoesNotContain(parameterTypes, t => t.Namespace?.StartsWith("ONEVO.Infrastructure", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(parameterTypes, t => t.Name == "IEmployeeVisibilityScopeResolver");
    }

    [Fact] // Requirement 2: the handler asks the resolver for EmployeeListRead visibility.
    public void ListEmployeesQueryHandler_UsesEmployeeListReadPurpose()
    {
        var path = FindFileUnderRepoRoot(
            "src", "ONEVO.Application", "Features", "CoreHr", "Employee", "Queries", "ListEmployees",
            "ListEmployeesQueryHandler.cs");
        var text = File.ReadAllText(path);

        Assert.Contains("EmployeeAuthorityPurpose.EmployeeListRead", text, StringComparison.Ordinal);
        Assert.Contains("\"employees:read\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"employees:write\"", text, StringComparison.Ordinal);
    }

    // The Employees directory remains coverage-scoped through the resolver - org:manage must never
    // bypass it. Attendance-sensitive fields use the separate attendance:read gate required by the
    // employee-list warning contract.
    [Fact]
    public void ListEmployeesQueryHandler_NeverChecksOrgManage_AndUsesAttendanceReadOnlyForAttendanceData()
    {
        var path = FindFileUnderRepoRoot(
            "src", "ONEVO.Application", "Features", "CoreHr", "Employee", "Queries", "ListEmployees",
            "ListEmployeesQueryHandler.cs");
        var text = File.ReadAllText(path);

        Assert.DoesNotContain("org:manage", text, StringComparison.Ordinal);
        Assert.Contains("HasPermission(AttendanceReadPermission)", text, StringComparison.Ordinal);
        Assert.Contains("\"attendance:read\"", text, StringComparison.Ordinal);
    }

    private static string FindFileUnderRepoRoot(params string[] relativeSegments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. relativeSegments]);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate " + Path.Combine(relativeSegments) + " above " + AppContext.BaseDirectory);
    }
}
