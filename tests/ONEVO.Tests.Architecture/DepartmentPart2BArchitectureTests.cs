using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.OrgStructure.Departments;
using ONEVO.Application.Features.OrgStructure.Commands.ArchiveDepartment;
using ONEVO.Application.Features.OrgStructure.Commands.CreateDepartment;
using ONEVO.Application.Features.OrgStructure.Commands.RestoreDepartment;
using ONEVO.Application.Features.OrgStructure.Commands.UpdateDepartment;
using ONEVO.Application.Features.OrgStructure.Queries.CheckDepartmentArchiveDependencies;
using ONEVO.Application.Features.OrgStructure.Queries.GetDepartment;
using ONEVO.Application.Features.OrgStructure.Queries.ListDepartments;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards Part 2B scope for Department: commands, queries, validators, DTOs,
/// and API request contracts. Asserts no controller/route exists yet, request contracts
/// exclude TenantId and HeadPositionId, CQRS handlers do not bypass repository abstractions
/// with ApplicationDbContext, files live under OrgStructure/Department, and schema/migrations
/// remain untouched.
/// </summary>
public class DepartmentPart2BArchitectureTests
{
    private static readonly Type[] RequestContractTypes =
    [
        typeof(CreateDepartmentRequest),
        typeof(UpdateDepartmentRequest)
    ];

    private static readonly Type[] CommandAndQueryTypes =
    [
        typeof(CreateDepartmentCommand),
        typeof(UpdateDepartmentCommand),
        typeof(ArchiveDepartmentCommand),
        typeof(RestoreDepartmentCommand),
        typeof(ListDepartmentsQuery),
        typeof(GetDepartmentQuery),
        typeof(CheckDepartmentArchiveDependenciesQuery)
    ];

    private static readonly Type[] HandlerTypes =
    [
        typeof(CreateDepartmentCommandHandler),
        typeof(UpdateDepartmentCommandHandler),
        typeof(ArchiveDepartmentCommandHandler),
        typeof(RestoreDepartmentCommandHandler),
        typeof(ListDepartmentsQueryHandler),
        typeof(GetDepartmentQueryHandler),
        typeof(CheckDepartmentArchiveDependenciesQueryHandler)
    ];

    // NoDepartmentsController_ExistsYetInPart2B and NoDepartmentApiRoute_AddedYetInPart2B were removed here:
    // they asserted no Department controller or route existed, which was correct for Part 2B scope
    // but is now obsolete now that Part 2C legitimately adds
    // Controllers/Tenant/OrgStructure/DepartmentsController.cs. Controller/route
    // and permission-mapping rules for that controller now live in
    // DepartmentsControllerArchitectureTests.cs.

    [Theory]
    [MemberData(nameof(AllRequestContractTypes))]
    public void RequestContracts_DoNotContainTenantId(Type contractType)
    {
        var hasTenantId = contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(p => string.Equals(p.Name, "TenantId", StringComparison.OrdinalIgnoreCase));

        Assert.False(hasTenantId, $"{contractType.Name} must not contain a TenantId property.");
    }

    // CreateAndUpdateRequestContracts_DoNotContainHeadPositionId was removed here: it asserted
    // the request contracts had no HeadPositionId property, which was correct for Part 2B scope
    // but is now obsolete now that Part 3 legitimately adds Guid? HeadPositionId to both
    // CreateDepartmentRequest and UpdateDepartmentRequest. Validation rules for the new field
    // now live in DepartmentPart3ArchitectureTests.cs.

    [Theory]
    [MemberData(nameof(AllHandlerTypes))]
    public void Handlers_DoNotUseApplicationDbContextDirectly(Type handlerType)
    {
        var constructors = handlerType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var parameterTypes = constructors.SelectMany(c => c.GetParameters()).Select(p => p.ParameterType);

        var usesDbContext = parameterTypes.Any(t => t.Name.Contains("DbContext", StringComparison.OrdinalIgnoreCase));

        Assert.False(usesDbContext, $"{handlerType.Name} must not inject ApplicationDbContext directly; use repository abstractions.");
    }

    [Theory]
    [InlineData("Commands/CreateDepartment", "CreateDepartmentCommand.cs")]
    [InlineData("Commands/CreateDepartment", "CreateDepartmentCommandHandler.cs")]
    [InlineData("Commands/CreateDepartment", "CreateDepartmentCommandValidator.cs")]
    [InlineData("Commands/UpdateDepartment", "UpdateDepartmentCommand.cs")]
    [InlineData("Commands/UpdateDepartment", "UpdateDepartmentCommandHandler.cs")]
    [InlineData("Commands/UpdateDepartment", "UpdateDepartmentCommandValidator.cs")]
    [InlineData("Commands/ArchiveDepartment", "ArchiveDepartmentCommand.cs")]
    [InlineData("Commands/ArchiveDepartment", "ArchiveDepartmentCommandHandler.cs")]
    [InlineData("Commands/ArchiveDepartment", "ArchiveDepartmentCommandValidator.cs")]
    [InlineData("Commands/RestoreDepartment", "RestoreDepartmentCommand.cs")]
    [InlineData("Commands/RestoreDepartment", "RestoreDepartmentCommandHandler.cs")]
    [InlineData("Commands/RestoreDepartment", "RestoreDepartmentCommandValidator.cs")]
    public void CommandFiles_LiveUnderOrgStructureDepartmentFolder(string subfolder, string fileName)
    {
        var deptRoot = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Application", "Features", "OrgStructure", "Department");
        var path = Path.Combine(deptRoot, subfolder, fileName);

        Assert.True(File.Exists(path), $"expected {path} to exist under OrgStructure/Department/{subfolder}");
    }

    [Theory]
    [InlineData("Queries/ListDepartments", "ListDepartmentsQuery.cs")]
    [InlineData("Queries/ListDepartments", "ListDepartmentsQueryHandler.cs")]
    [InlineData("Queries/ListDepartments", "ListDepartmentsQueryValidator.cs")]
    [InlineData("Queries/GetDepartment", "GetDepartmentQuery.cs")]
    [InlineData("Queries/GetDepartment", "GetDepartmentQueryHandler.cs")]
    [InlineData("Queries/GetDepartment", "GetDepartmentQueryValidator.cs")]
    [InlineData("Queries/CheckDepartmentArchiveDependencies", "CheckDepartmentArchiveDependenciesQuery.cs")]
    [InlineData("Queries/CheckDepartmentArchiveDependencies", "CheckDepartmentArchiveDependenciesQueryHandler.cs")]
    [InlineData("Queries/CheckDepartmentArchiveDependencies", "CheckDepartmentArchiveDependenciesQueryValidator.cs")]
    public void QueryFiles_LiveUnderOrgStructureDepartmentFolder(string subfolder, string fileName)
    {
        var deptRoot = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Application", "Features", "OrgStructure", "Department");
        var path = Path.Combine(deptRoot, subfolder, fileName);

        Assert.True(File.Exists(path), $"expected {path} to exist under OrgStructure/Department/{subfolder}");
    }

    [Fact]
    public void DepartmentPart2A_HeadPositionIdSchema_RemainsPresent()
    {
        var deptType = typeof(ONEVO.Domain.Features.OrgStructure.Entities.Department);
        var property = deptType.GetProperty("HeadPositionId");

        Assert.NotNull(property);
        Assert.Equal(typeof(Guid?), property.PropertyType);
    }

    [Fact]
    public void DepartmentApplicationLayer_DoesNotUseDateTimeOffsetUtcNowDirectly()
    {
        var deptAppRoot = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Application", "Features", "OrgStructure", "Department");

        var csFiles = Directory.GetFiles(deptAppRoot, "*.cs", SearchOption.AllDirectories);

        var offendingFiles = new List<string>();
        foreach (var file in csFiles)
        {
            var text = File.ReadAllText(file);
            if (text.Contains("DateTimeOffset.UtcNow"))
            {
                offendingFiles.Add(Path.GetFileName(file));
            }
        }

        Assert.True(
            offendingFiles.Count == 0,
            "Department Application layer must use IDateTimeProvider rather than DateTimeOffset.UtcNow, but found matches in: " + string.Join(", ", offendingFiles));
    }

    public static IEnumerable<object[]> AllRequestContractTypes()
        => RequestContractTypes.Select(t => new object[] { t });

    public static IEnumerable<object[]> AllCommandAndQueryTypes()
        => CommandAndQueryTypes.Select(t => new object[] { t });

    public static IEnumerable<object[]> AllHandlerTypes()
        => HandlerTypes.Select(t => new object[] { t });

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
