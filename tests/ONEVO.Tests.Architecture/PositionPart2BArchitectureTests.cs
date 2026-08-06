using System.Reflection;
using ONEVO.Api.Contracts.OrgStructure.Positions;
using ONEVO.Application.Features.OrgStructure.Commands.ArchivePosition;
using ONEVO.Application.Features.OrgStructure.Commands.CheckPositionArchive;
using ONEVO.Application.Features.OrgStructure.Commands.CreatePosition;
using ONEVO.Application.Features.OrgStructure.Commands.RestorePosition;
using ONEVO.Application.Features.OrgStructure.Commands.UpdatePosition;
using ONEVO.Application.Features.OrgStructure.Queries.GetPositionById;
using ONEVO.Application.Features.OrgStructure.Queries.GetPositionTree;
using ONEVO.Application.Features.OrgStructure.Queries.ListPositions;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards Part 2B scope for Position: commands, queries, validators, DTOs, mappers, repository
/// additions, and API request contracts. Asserts no controller/route exists yet, request
/// contracts and commands exclude ownership/role/occupant fields, CQRS handlers do not bypass
/// repository abstractions with ApplicationDbContext, no new C# enum is introduced for
/// type/sort/status, and every file lives under the expected OrgStructure/Position path.
/// </summary>
public class PositionPart2BArchitectureTests
{
    private static readonly Type[] RequestContractTypes =
    [
        typeof(CreatePositionRequest),
        typeof(UpdatePositionRequest)
    ];

    private static readonly Type[] CommandTypes =
    [
        typeof(CreatePositionCommand),
        typeof(UpdatePositionCommand),
        typeof(ArchivePositionCommand),
        typeof(RestorePositionCommand),
        typeof(CheckPositionArchiveCommand)
    ];

    private static readonly Type[] HandlerTypes =
    [
        typeof(CreatePositionCommandHandler),
        typeof(UpdatePositionCommandHandler),
        typeof(ArchivePositionCommandHandler),
        typeof(RestorePositionCommandHandler),
        typeof(CheckPositionArchiveCommandHandler),
        typeof(GetPositionByIdQueryHandler),
        typeof(ListPositionsQueryHandler),
        typeof(GetPositionTreeQueryHandler)
    ];

    private static readonly string[] ForbiddenContractPropertyNames =
    [
        "TenantId", "LegalEntityId", "Role", "Permission", "Occupant", "Assignment",
        "HeadOfDepartment", "IsDepartmentHead", "HeadPositionId"
    ];

    private static readonly string[] ForbiddenCommandPropertyNames =
    [
        "Role", "Permission", "Occupant", "Assignment", "HeadOfDepartment", "IsDepartmentHead", "HeadPositionId"
    ];

    private static readonly Assembly ApplicationAssembly = typeof(IPositionRepository).Assembly;
    private static readonly Assembly ApiAssembly = typeof(ONEVO.Api.Controllers.Tenant.OrgStructure.DepartmentsController).Assembly;

    [Theory]
    [MemberData(nameof(AllRequestContractTypes))]
    public void RequestContracts_DoNotContainForbiddenOwnershipOrRoleFields(Type contractType)
    {
        var offendingProperties = contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => ForbiddenContractPropertyNames.Any(forbidden =>
                p.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToList();

        Assert.True(
            offendingProperties.Count == 0,
            $"{contractType.Name} must not expose: {string.Join(", ", offendingProperties)}");
    }

    [Theory]
    [MemberData(nameof(AllCommandTypes))]
    public void Commands_DoNotContainForbiddenRoleOrOccupantFields(Type commandType)
    {
        var offendingProperties = commandType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => ForbiddenCommandPropertyNames.Any(forbidden =>
                p.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToList();

        Assert.True(
            offendingProperties.Count == 0,
            $"{commandType.Name} must not expose: {string.Join(", ", offendingProperties)}");
    }

    [Theory]
    [MemberData(nameof(AllHandlerTypes))]
    public void Handlers_DoNotUseApplicationDbContextDirectly(Type handlerType)
    {
        var constructors = handlerType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var parameterTypes = constructors.SelectMany(c => c.GetParameters()).Select(p => p.ParameterType);

        var usesDbContext = parameterTypes.Any(t => t.Name.Contains("DbContext", StringComparison.OrdinalIgnoreCase));

        Assert.False(usesDbContext, $"{handlerType.Name} must not inject ApplicationDbContext directly; use repository abstractions.");
    }

    [Fact]
    public void PositionsController_IntroducedInPart2C_IsTheOnlyPositionController()
    {
        // Part 2B asserted no PositionsController existed yet. Part 2C introduced exactly one
        // (ONEVO.Api.Controllers.Tenant.OrgStructure.PositionsController) - see
        // POSITION_FOUNDATION_PART2C_CONTROLLER_ENDPOINTS_REPORT.md. This guard now asserts
        // there is exactly one, not zero, so a stray duplicate controller cannot slip in later.
        var controllers = ApiAssembly.GetTypes()
            .Where(t => t.Name.Contains("Position", StringComparison.OrdinalIgnoreCase) && t.Name.EndsWith("Controller", StringComparison.Ordinal))
            .ToList();

        Assert.Single(controllers);
        Assert.Equal("ONEVO.Api.Controllers.Tenant.OrgStructure.PositionsController", controllers[0].FullName);
    }

    [Fact]
    public void ApplicationAssembly_DoesNotDeclare_PositionNamedEnums()
    {
        // Scoped to OrgStructure namespaces deliberately: the Application assembly also holds
        // unrelated features (billing, OAuth apps, config templates, MFA, ...) that could
        // coincidentally declare an enum containing "Position" in its name for a reason that has
        // nothing to do with this plan's type/sort/status constraint. Scanning the whole assembly
        // would make this test a false-positive trap for other teams' unrelated work.
        var offendingEnums = ApplicationAssembly.GetTypes()
            .Where(t => t.IsEnum
                && t.Name.Contains("Position", StringComparison.OrdinalIgnoreCase)
                && t.Namespace is not null
                && t.Namespace.Contains("OrgStructure", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offendingEnums.Count == 0, "No enum type may be introduced for Position type/sort/status: " + string.Join(", ", offendingEnums));
    }

    [Fact]
    public void ApiAssembly_DoesNotDeclare_PositionNamedEnums()
    {
        var offendingEnums = ApiAssembly.GetTypes()
            .Where(t => t.IsEnum
                && t.Name.Contains("Position", StringComparison.OrdinalIgnoreCase)
                && t.Namespace is not null
                && t.Namespace.Contains("OrgStructure", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offendingEnums.Count == 0, "No enum type may be introduced for Position type/sort/status: " + string.Join(", ", offendingEnums));
    }

    [Theory]
    [InlineData("Commands/CreatePosition", "CreatePositionCommand.cs")]
    [InlineData("Commands/CreatePosition", "CreatePositionCommandHandler.cs")]
    [InlineData("Commands/CreatePosition", "CreatePositionCommandValidator.cs")]
    [InlineData("Commands/UpdatePosition", "UpdatePositionCommand.cs")]
    [InlineData("Commands/UpdatePosition", "UpdatePositionCommandHandler.cs")]
    [InlineData("Commands/UpdatePosition", "UpdatePositionCommandValidator.cs")]
    [InlineData("Commands/ArchivePosition", "ArchivePositionCommand.cs")]
    [InlineData("Commands/ArchivePosition", "ArchivePositionCommandHandler.cs")]
    [InlineData("Commands/ArchivePosition", "ArchivePositionCommandValidator.cs")]
    [InlineData("Commands/RestorePosition", "RestorePositionCommand.cs")]
    [InlineData("Commands/RestorePosition", "RestorePositionCommandHandler.cs")]
    [InlineData("Commands/RestorePosition", "RestorePositionCommandValidator.cs")]
    [InlineData("Commands/CheckPositionArchive", "CheckPositionArchiveCommand.cs")]
    [InlineData("Commands/CheckPositionArchive", "CheckPositionArchiveCommandHandler.cs")]
    [InlineData("Commands/CheckPositionArchive", "CheckPositionArchiveCommandValidator.cs")]
    [InlineData("Queries/GetPositionById", "GetPositionByIdQuery.cs")]
    [InlineData("Queries/GetPositionById", "GetPositionByIdQueryHandler.cs")]
    [InlineData("Queries/GetPositionById", "GetPositionByIdQueryValidator.cs")]
    [InlineData("Queries/ListPositions", "ListPositionsQuery.cs")]
    [InlineData("Queries/ListPositions", "ListPositionsQueryHandler.cs")]
    [InlineData("Queries/ListPositions", "ListPositionsQueryValidator.cs")]
    [InlineData("Queries/GetPositionTree", "GetPositionTreeQuery.cs")]
    [InlineData("Queries/GetPositionTree", "GetPositionTreeQueryHandler.cs")]
    [InlineData("Queries/GetPositionTree", "GetPositionTreeQueryValidator.cs")]
    [InlineData("Responses", "PositionResponse.cs")]
    [InlineData("Responses", "PositionListItemResponse.cs")]
    [InlineData("Responses", "PositionTreeNodeResponse.cs")]
    [InlineData("Responses", "PositionPageResponse.cs")]
    [InlineData("Responses", "PositionArchiveBlockers.cs")]
    [InlineData("Mappers", "PositionMapper.cs")]
    [InlineData("Mappers", "PositionTreeMapper.cs")]
    [InlineData("RepositoryInterfaces", "PositionPage.cs")]
    [InlineData("Services", "PositionArchiveDependencyEvaluator.cs")]
    public void Part2BFiles_LiveUnderOrgStructurePositionFolder(string subfolder, string fileName)
    {
        var positionRoot = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Application", "Features", "OrgStructure", "Position");
        var path = Path.Combine(positionRoot, subfolder, fileName);

        Assert.True(File.Exists(path), $"expected {path} to exist under OrgStructure/Position/{subfolder}");
    }

    [Fact]
    public void PositionApplicationLayer_DoesNotUseDateTimeOffsetUtcNowDirectly()
    {
        var positionAppRoot = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Application", "Features", "OrgStructure", "Position");

        var csFiles = Directory.GetFiles(positionAppRoot, "*.cs", SearchOption.AllDirectories);

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
            "Position Application layer must use IDateTimeProvider rather than DateTimeOffset.UtcNow, but found matches in: " + string.Join(", ", offendingFiles));
    }

    [Fact]
    public void CreateUpdateAndRestorePositionHandlers_BlockInactiveLegalEntity()
    {
        var positionAppRoot = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Application", "Features", "OrgStructure", "Position");

        var filesToCheck = new[]
        {
            Path.Combine(positionAppRoot, "Commands", "CreatePosition", "CreatePositionCommandHandler.cs"),
            Path.Combine(positionAppRoot, "Commands", "UpdatePosition", "UpdatePositionCommandHandler.cs"),
            Path.Combine(positionAppRoot, "Commands", "RestorePosition", "RestorePositionCommandHandler.cs"),
        };

        var missing = filesToCheck
            .Where(f => !File.ReadAllText(f).Contains("legalEntity.IsActive"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "Create/Update/Restore position handlers must all check legalEntity.IsActive before proceeding, but missing in: "
                + string.Join(", ", missing));
    }

    public static IEnumerable<object[]> AllRequestContractTypes()
        => RequestContractTypes.Select(t => new object[] { t });

    public static IEnumerable<object[]> AllCommandTypes()
        => CommandTypes.Select(t => new object[] { t });

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
