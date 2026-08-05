using System.Text.RegularExpressions;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards Department Hardening Part 2 scope: archive dependency checks and restore.
/// No physical delete of departments, the new archive-check/restore files use
/// IDateTimeProvider instead of DateTimeOffset.UtcNow, no Position controller was added,
/// no role/permission-management code was added, and Delete/Archive both delegate to the
/// same ArchiveDepartmentCommand (so the DELETE alias inherits the new blocker check too).
/// </summary>
public sealed class DepartmentPart2ArchiveRestoreArchitectureTests
{
    [Fact]
    public void EfDepartmentRepository_NeverCallsRemoveOnDepartmentsDbSet()
    {
        var source = ReadRepositorySource();
        Assert.DoesNotContain("_db.Departments.Remove", source);
        Assert.DoesNotContain(".Remove(department", source);
    }

    [Fact]
    public void RestoreAndCheckArchiveHandlers_DoNotUseDateTimeOffsetUtcNowDirectly()
    {
        var deptAppRoot = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Application", "Features", "OrgStructure", "Department");

        var targetFiles = new[]
        {
            Path.Combine(deptAppRoot, "Commands", "RestoreDepartment", "RestoreDepartmentCommandHandler.cs"),
            Path.Combine(deptAppRoot, "Commands", "ArchiveDepartment", "ArchiveDepartmentCommandHandler.cs"),
            Path.Combine(
                deptAppRoot, "Queries", "CheckDepartmentArchiveDependencies",
                "CheckDepartmentArchiveDependenciesQueryHandler.cs"),
        };

        foreach (var file in targetFiles)
        {
            Assert.True(File.Exists(file), $"expected {file} to exist");
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("DateTimeOffset.UtcNow", text);
        }
    }

    [Fact]
    public void PositionsController_FromPart2C_LivesInTenantOrgStructureNamespace()
    {
        // This guard originally asserted PositionsController did not exist yet (pre Part 2C).
        // Position Foundation Part 2C introduced ONEVO.Api.Controllers.Tenant.OrgStructure.
        // PositionsController - see POSITION_FOUNDATION_PART2C_CONTROLLER_ENDPOINTS_REPORT.md.
        // Renamed from NoPositionController_HasBeenAddedInPart2 so the name matches the
        // assertion direction (exists, expected namespace) instead of contradicting it.
        var apiAssembly = typeof(ONEVO.Api.Controllers.Tenant.OrgStructure.DepartmentsController).Assembly;
        var match = apiAssembly.GetTypes()
            .FirstOrDefault(t => t.Name.Equals("PositionsController", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(match);
        Assert.Equal("ONEVO.Api.Controllers.Tenant.OrgStructure", match!.Namespace);
    }

    [Fact]
    public void NoRoleOrPermissionManagementCode_WasAddedForThisFeature()
    {
        var deptAppRoot = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Application", "Features", "OrgStructure", "Department");

        var newFolders = new[]
        {
            Path.Combine(deptAppRoot, "Commands", "RestoreDepartment"),
            Path.Combine(deptAppRoot, "Queries", "CheckDepartmentArchiveDependencies"),
        };

        foreach (var folder in newFolders)
        {
            Assert.True(Directory.Exists(folder), $"expected {folder} to exist");
            foreach (var file in Directory.GetFiles(folder, "*.cs"))
            {
                var text = File.ReadAllText(file);
                Assert.DoesNotContain("RoleTemplate", text);
                Assert.DoesNotContain("CreateRole", text);
                Assert.DoesNotContain("PermissionSeeder", text);
            }
        }
    }

    [Fact]
    public void DeleteAndArchiveActions_BothDelegateToArchiveDepartmentCommand()
    {
        var source = ReadControllerSource();
        var occurrences = Regex.Matches(source, @"new ArchiveDepartmentCommand\(").Count;
        Assert.Equal(2, occurrences);
    }

    private static string ReadControllerSource()
    {
        var path = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Api", "Controllers", "Tenant", "OrgStructure");
        return File.ReadAllText(Path.Combine(path, "DepartmentsController.cs"));
    }

    private static string ReadRepositorySource()
    {
        var path = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Infrastructure", "Persistence", "Repositories", "OrgStructure", "Department");
        return File.ReadAllText(Path.Combine(path, "EfDepartmentRepository.cs"));
    }

    private static string FindDirectoryUnderRepoRoot(params string[] relativeSegments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. relativeSegments]);
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate " + Path.Combine(relativeSegments) + " above " + AppContext.BaseDirectory);
    }
}
