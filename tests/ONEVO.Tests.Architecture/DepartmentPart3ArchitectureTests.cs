using System.Reflection;
using ONEVO.Api.Contracts.OrgStructure.Departments;
using ONEVO.Application.Features.OrgStructure.Commands.CreateDepartment;
using ONEVO.Application.Features.OrgStructure.Commands.UpdateDepartment;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards Department Part 3 scope: Department create/update request contracts and commands may
/// now carry Guid? HeadPositionId (Position Foundation is complete), but legal entity scope must
/// still come only from the route, and no role/access/user-assignment fields may be introduced.
/// </summary>
public class DepartmentPart3ArchitectureTests
{
    [Fact]
    public void PositionEntity_HasDepartmentIdProperty()
    {
        var property = typeof(Position).GetProperty("DepartmentId");

        Assert.NotNull(property);
        Assert.Equal(typeof(Guid?), property.PropertyType);
    }

    [Theory]
    [InlineData(typeof(CreateDepartmentRequest))]
    [InlineData(typeof(UpdateDepartmentRequest))]
    public void RequestContracts_ExposeHeadPositionId_AsNullableGuid(Type contractType)
    {
        var property = contractType.GetProperty("HeadPositionId");

        Assert.NotNull(property);
        Assert.Equal(typeof(Guid?), property!.PropertyType);
    }

    [Theory]
    [InlineData(typeof(CreateDepartmentRequest))]
    [InlineData(typeof(UpdateDepartmentRequest))]
    public void RequestContracts_StillDoNotExposeTenantIdOrLegalEntityId(Type contractType)
    {
        var properties = contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain(properties, name => string.Equals(name, "TenantId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => string.Equals(name, "LegalEntityId", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(typeof(CreateDepartmentCommand))]
    [InlineData(typeof(UpdateDepartmentCommand))]
    public void Commands_ExposeHeadPositionId_AsNullableGuid(Type commandType)
    {
        var property = commandType.GetProperty("HeadPositionId");

        Assert.NotNull(property);
        Assert.Equal(typeof(Guid?), property!.PropertyType);
    }

    [Theory]
    [InlineData(typeof(CreateDepartmentRequest))]
    [InlineData(typeof(UpdateDepartmentRequest))]
    [InlineData(typeof(CreateDepartmentCommand))]
    [InlineData(typeof(UpdateDepartmentCommand))]
    public void DepartmentContractsAndCommands_ExposeNoRoleOrAccessOrUserAssignmentFields(Type type)
    {
        var offendingSubstrings = new[] { "Role", "Permission", "AccessTemplate", "UserId", "Employee" };

        var offenders = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => offendingSubstrings.Any(s => p.Name.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToList();

        Assert.True(offenders.Count == 0, $"{type.Name} must not expose role/access/user-assignment fields, but found: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void DepartmentApplicationLayer_DoesNotUseGuidEmptyFallback_ForHeadPositionId()
    {
        var deptAppRoot = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Application", "Features", "OrgStructure", "Department");

        var csFiles = Directory.GetFiles(deptAppRoot, "*.cs", SearchOption.AllDirectories);

        var offendingFiles = new List<string>();
        foreach (var file in csFiles)
        {
            var text = File.ReadAllText(file);
            if (text.Contains("HeadPositionId ?? Guid.Empty"))
            {
                offendingFiles.Add(Path.GetFileName(file));
            }
        }

        Assert.True(
            offendingFiles.Count == 0,
            "Department Application layer must not use a Guid.Empty fallback for HeadPositionId, but found matches in: " + string.Join(", ", offendingFiles));
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
