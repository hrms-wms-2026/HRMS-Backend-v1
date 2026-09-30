using Xunit;

namespace ONEVO.Tests.Architecture;

public sealed class EmployeesControllerArchitectureTests
{
    [Fact]
    public void EmployeesController_DoesNotReferenceApplicationDbContextOrRepositoriesDirectly()
    {
        var path = FindRepositoryPath(
            "src", "ONEVO.Api", "Controllers", "Tenant", "CoreHr", "EmployeesController.cs");
        var source = File.ReadAllText(path);

        Assert.DoesNotContain("ApplicationDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EfEmployeeRepository", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IEmployeeRepository", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EmployeesController_OnlyDependsOnIMediatorConstructorParameter()
    {
        var path = FindRepositoryPath(
            "src", "ONEVO.Api", "Controllers", "Tenant", "CoreHr", "EmployeesController.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("private readonly IMediator _mediator;", source, StringComparison.Ordinal);
    }

    // EMPLOYEE_LIST_AUTHORITY_RESOLVER_BACKEND_PART1 requirement 17/18: the List action keeps
    // requiring employees:read (unchanged from before this task - see the report's "self
    // visibility decision" section for why this was not loosened) and takes no tenantId
    // parameter from the query string or request body; tenant context always comes from
    // ICurrentUser inside the handler.
    [Fact]
    public void EmployeesController_List_StillRequiresEmployeesReadPermission()
    {
        var path = FindRepositoryPath(
            "src", "ONEVO.Api", "Controllers", "Tenant", "CoreHr", "EmployeesController.cs");
        var source = File.ReadAllText(path);

        var listActionIndex = source.IndexOf("public async Task<IActionResult> List(", StringComparison.Ordinal);
        Assert.True(listActionIndex > 0, "Could not locate the List action.");

        var precedingSource = source[..listActionIndex];
        var lastAttributeIndex = precedingSource.LastIndexOf("[RequirePermission(\"employees:read\")]", StringComparison.Ordinal);
        Assert.True(lastAttributeIndex > 0, "List action is missing [RequirePermission(\"employees:read\")].");
    }

    [Fact]
    public void EmployeesController_List_DoesNotAcceptATenantIdParameter()
    {
        var path = FindRepositoryPath(
            "src", "ONEVO.Api", "Controllers", "Tenant", "CoreHr", "EmployeesController.cs");
        var source = File.ReadAllText(path);

        var listActionIndex = source.IndexOf("public async Task<IActionResult> List(", StringComparison.Ordinal);
        var listActionEnd = source.IndexOf(")", listActionIndex, StringComparison.Ordinal);
        var signature = source[listActionIndex..listActionEnd];

        Assert.DoesNotContain("tenantId", signature, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmployeesController_GetWorkGraph_RequiresEmployeesReadAndUsesTheWorkGraphRoute()
    {
        var path = FindRepositoryPath(
            "src", "ONEVO.Api", "Controllers", "Tenant", "CoreHr", "EmployeesController.cs");
        var source = File.ReadAllText(path);

        var actionIndex = source.IndexOf("public async Task<IActionResult> GetWorkGraph(", StringComparison.Ordinal);
        Assert.True(actionIndex > 0, "Could not locate the GetWorkGraph action.");

        var preceding = source[..actionIndex];
        var routeIndex = preceding.LastIndexOf("[HttpGet(\"{id:guid}/work-graph\")]", StringComparison.Ordinal);
        var permissionIndex = preceding.LastIndexOf("[RequirePermission(\"employees:read\")]", StringComparison.Ordinal);
        Assert.True(routeIndex > 0, "GetWorkGraph is missing [HttpGet(\"{id:guid}/work-graph\")].");
        Assert.True(permissionIndex > routeIndex, "GetWorkGraph is missing [RequirePermission(\"employees:read\")] after its route attribute.");
    }

    [Theory]
    [InlineData("overview/attendance", "GetOverviewAttendance")]
    [InlineData("overview/attendance-discipline", "GetOverviewAttendanceDiscipline")]
    [InlineData("overview/time-off", "GetOverviewTimeOff")]
    [InlineData("overview/work", "GetOverviewWork")]
    [InlineData("overview/delivery", "GetOverviewDelivery")]
    [InlineData("overview/activity", "GetOverviewActivity")]
    [InlineData("overview/approvals", "GetOverviewApprovals")]
    [InlineData("overview/checklists", "GetOverviewChecklists")]
    [InlineData("overview/history", "GetOverviewHistory")]
    [InlineData("overview/upcoming", "GetOverviewUpcoming")]
    public void EmployeesController_OverviewWidgetActions_RequireEmployeesReadAndUseTheirRoute(string route, string action)
    {
        var path = FindRepositoryPath(
            "src", "ONEVO.Api", "Controllers", "Tenant", "CoreHr", "EmployeesController.cs");
        var source = File.ReadAllText(path);

        var actionIndex = source.IndexOf($"public async Task<IActionResult> {action}(", StringComparison.Ordinal);
        Assert.True(actionIndex > 0, $"Could not locate the {action} action.");

        var preceding = source[..actionIndex];
        var routeIndex = preceding.LastIndexOf($"[HttpGet(\"{{id:guid}}/{route}\")]", StringComparison.Ordinal);
        var permissionIndex = preceding.LastIndexOf("[RequirePermission(\"employees:read\")]", StringComparison.Ordinal);
        Assert.True(routeIndex > 0, $"{action} is missing its [HttpGet] route for {route}.");
        Assert.True(permissionIndex > routeIndex, $"{action} is missing [RequirePermission(\"employees:read\")] after its route attribute.");
    }

    private static string FindRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var repositoryMarker = Path.Combine(directory.FullName, "src", "ONEVO.Api");
            if (Directory.Exists(repositoryMarker))
            {
                return Path.Combine([directory.FullName, .. segments]);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
