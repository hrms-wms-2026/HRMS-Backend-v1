using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Task 27 found that per-employee monitoring reads checked only monitoring:read, so a holder could
/// read any employee in the tenant by changing ?employeeId=. Every tenant monitoring action that
/// takes a required employeeId from the query string must also carry
/// [RequireVisibleEmployee("employeeId")], the coverage gate /employees/{id}/overview/activity uses.
/// Optional filters (GET /monitoring/screenshots) are checked in their handler instead.
/// </summary>
public class MonitoringEmployeeCoverageArchitectureTests
{
    private const string MonitoringNamespace = "ONEVO.Api.Controllers.Tenant.Monitoring";

    [Fact]
    public void MonitoringActions_WithRequiredEmployeeIdQuery_RequireVisibleEmployee()
    {
        var actions = typeof(RequireVisibleEmployeeAttribute).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith(MonitoringNamespace, StringComparison.Ordinal) == true
                        && typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(m => m.GetParameters().Any(p =>
                p.Name == "employeeId"
                && p.ParameterType == typeof(Guid)
                && p.GetCustomAttribute<FromQueryAttribute>() is not null))
            .ToList();

        Assert.NotEmpty(actions);

        var ungated = actions
            .Where(m => m.GetCustomAttribute<RequireVisibleEmployeeAttribute>()?.RouteKey != "employeeId")
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        Assert.True(ungated.Count == 0,
            "Monitoring actions with ?employeeId= must carry [RequireVisibleEmployee(\"employeeId\")]: "
            + string.Join(", ", ungated));
    }
}
