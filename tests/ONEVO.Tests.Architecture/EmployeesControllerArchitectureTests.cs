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
