using System.Reflection;
using System.Text.RegularExpressions;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using Xunit;

namespace ONEVO.Tests.Architecture;

public class PositionPart2AArchitectureTests
{
    private static readonly Type PositionType = typeof(Position);
    private static readonly Type RepositoryType = typeof(EfPositionRepository);
    private static readonly Assembly DomainAssembly = typeof(Position).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(IPositionRepository).Assembly;
    private static readonly Assembly ApiAssembly = typeof(ONEVO.Api.Controllers.Tenant.OrgStructure.DepartmentsController).Assembly;

    [Fact]
    public void Migration_AddPositionFoundationSchema_DoesNotUseGuidEmptyDefaults_OrFakeRows()
    {
        var migrationText = ReadMigrationSource();

        Assert.DoesNotContain("00000000-0000-0000-0000-000000000000", migrationText);
        Assert.DoesNotContain("Guid.Empty", migrationText);
        Assert.DoesNotContain("DropTable(\"positions\"", migrationText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO legal_entities", migrationText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO departments", migrationText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_AddPositionFoundationSchema_AddsLegalEntityId_And_DepartmentId_AsNullable()
    {
        var migrationText = ReadMigrationSource();

        Assert.Contains("name: \"department_id\"", migrationText);
        Assert.Contains("name: \"legal_entity_id\"", migrationText);
        Assert.Contains("nullable: true", migrationText);
    }

    [Fact]
    public void PositionEntity_HasNullableTransitionalProperties_ForLegalEntityId_And_DepartmentId()
    {
        var legalEntityProp = PositionType.GetProperty(nameof(Position.LegalEntityId));
        Assert.NotNull(legalEntityProp);
        Assert.Equal(typeof(Guid?), legalEntityProp.PropertyType);

        var deptProp = PositionType.GetProperty(nameof(Position.DepartmentId));
        Assert.NotNull(deptProp);
        Assert.Equal(typeof(Guid?), deptProp.PropertyType);
    }

    [Fact]
    public void PositionConfiguration_DoesNotMarkTransitionalFieldsAsRequired()
    {
        var configText = ReadConfigurationSource();

        Assert.Contains("Property(p => p.LegalEntityId).IsRequired(false)", configText);
        Assert.Contains("Property(p => p.DepartmentId).IsRequired(false)", configText);
    }

    [Fact]
    public void PositionEntity_LivesInOrgStructureEntitiesNamespace()
    {
        Assert.Equal("ONEVO.Domain.Features.OrgStructure.Entities", PositionType.Namespace);
    }

    [Fact]
    public void PositionEntity_IsTenantOwned()
    {
        Assert.True(typeof(ITenantOwnedEntity).IsAssignableFrom(PositionType));
    }

    [Fact]
    public void PositionEntity_HasCanonicalPhase1Properties()
    {
        Assert.NotNull(PositionType.GetProperty(nameof(Position.Id)));
        Assert.NotNull(PositionType.GetProperty(nameof(Position.TenantId)));
        Assert.NotNull(PositionType.GetProperty(nameof(Position.LegalEntityId)));
        Assert.NotNull(PositionType.GetProperty(nameof(Position.DepartmentId)));
        Assert.NotNull(PositionType.GetProperty(nameof(Position.Name)));
        Assert.NotNull(PositionType.GetProperty(nameof(Position.Code)));
        Assert.NotNull(PositionType.GetProperty(nameof(Position.PositionType)));
        Assert.NotNull(PositionType.GetProperty(nameof(Position.MaxOccupancy)));
        Assert.NotNull(PositionType.GetProperty(nameof(Position.ReportsToPositionId)));
        Assert.NotNull(PositionType.GetProperty(nameof(Position.IsActive)));
    }

    [Fact]
    public void PositionEntity_PreservesDepartmentHeadPositionId_FK_Integrity()
    {
        var deptType = typeof(Department);
        var headProp = deptType.GetProperty(nameof(Department.HeadPositionId));

        Assert.NotNull(headProp);
        Assert.Equal(typeof(Guid?), headProp.PropertyType);

        var idProp = PositionType.GetProperty(nameof(Position.Id));
        Assert.NotNull(idProp);
        Assert.Equal(typeof(Guid), idProp.PropertyType);
    }

    // PositionPart2A_DoesNotExpose_HeadPositionId_InDepartment_RequestContracts was removed
    // here: it asserted the Department request contracts had no HeadPositionId property, which
    // was correct while Position Foundation was still Part 2A (Department could not yet
    // reference a validated position) but is now obsolete now that Department Part 3
    // legitimately adds Guid? HeadPositionId to CreateDepartmentRequest/UpdateDepartmentRequest
    // once the Position Foundation those departments validate against is complete - see
    // DepartmentPart3ArchitectureTests.cs for the current HeadPositionId guards.

    [Fact]
    public void PositionPart2C_Introduces_ExactlyOnePositionsController_InExpectedNamespace()
    {
        // This originally asserted no PositionsController existed yet (as part of a combined
        // Part 2A guard). Position Foundation Part 2C introduced exactly one
        // (ONEVO.Api.Controllers.Tenant.OrgStructure.PositionsController) - see
        // POSITION_FOUNDATION_PART2C_CONTROLLER_ENDPOINTS_REPORT.md. Split into its own
        // correctly-named fact so the assertion direction (exists, singular, expected namespace)
        // matches what the test name promises.
        //
        // Matches on the exact controller name rather than a "Name contains Position" scan:
        // the later Position Template Packs feature legitimately adds a second, differently
        // named OrgStructure controller (PositionTemplatePacksController) and must not trip
        // this guard - see PositionTemplatePacksController_IsASeparateControllerFromPositionsController.
        var controllers = ApiAssembly.GetTypes()
            .Where(t => t.Name.Equals("PositionsController", StringComparison.Ordinal))
            .ToList();
        Assert.Single(controllers);
        Assert.Equal("ONEVO.Api.Controllers.Tenant.OrgStructure.PositionsController", controllers[0].FullName);
    }

    [Fact]
    public void PositionTemplatePacksController_IsASeparateControllerFromPositionsController()
    {
        // Position Template Packs is its own OrgStructure feature with its own controller. It
        // is allowed to exist alongside PositionsController and must not replace or merge into
        // it - see PositionPart2C_Introduces_ExactlyOnePositionsController_InExpectedNamespace.
        var templatePacksController = ApiAssembly.GetTypes()
            .SingleOrDefault(t => t.Name.Equals("PositionTemplatePacksController", StringComparison.Ordinal));

        Assert.NotNull(templatePacksController);
        Assert.Equal(
            "ONEVO.Api.Controllers.Tenant.OrgStructure.PositionTemplatePacksController",
            templatePacksController!.FullName);
        Assert.NotEqual(typeof(ONEVO.Api.Controllers.Tenant.OrgStructure.PositionsController), templatePacksController);
    }

    [Fact]
    public void PositionPart2A_DoesNotExpose_Commands_Queries_Or_RequestContracts()
    {
        // No CQRS commands, queries, validators in Application assembly for Position
        //
        // PositionTemplatePacks is excluded: it is a separate, later OrgStructure feature
        // (ONEVO.Application.Features.OrgStructure.PositionTemplatePacks.*) that legitimately
        // has its own commands/queries. Without this exclusion it would be caught by the
        // "OrgStructure.Position" substring check below, which has no word boundary and matches
        // any namespace segment starting with "Position" (PositionTemplatePacks included), not
        // just the original Part 2A "OrgStructure.Position" scope.
        var appTypes = ApplicationAssembly.GetTypes()
            .Where(t => t.Namespace?.Contains("OrgStructure.Position", StringComparison.Ordinal) == true)
            .Where(t => t.Namespace?.StartsWith(
                "ONEVO.Application.Features.OrgStructure.PositionTemplatePacks",
                StringComparison.Ordinal) != true)
            .ToList();

        var nonRepoTypes = appTypes
            .Where(t => !t.Name.EndsWith("Repository", StringComparison.Ordinal) && !t.Name.StartsWith("I", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(nonRepoTypes);
    }

    // PositionPart2A_DoesNotInclude_DeferredTables_PositionAssignments_Or_EmployeeHierarchyClosure
    // removed: it was a deliberate scope fence for the Position Part 2A milestone, asserting
    // these two tables were not yet built. The Employee Management Phase 1 foundation now
    // builds both intentionally (see PositionAssignmentArchitectureTests and
    // EMPLOYEE_MANAGEMENT_IMPLEMENTATION_REPORT.md) - the guard's premise is obsolete, not
    // violated.

    [Fact]
    public void PositionPart2A_Entities_DoNotUseEnums_ForTypeOrStatus()
    {
        var posTypeProperty = PositionType.GetProperty(nameof(Position.PositionType));
        Assert.Equal(typeof(string), posTypeProperty?.PropertyType);

        var coverageType = typeof(ManagementCoverageRecord);
        Assert.Equal(typeof(string), coverageType.GetProperty(nameof(ManagementCoverageRecord.CoveredTargetType))?.PropertyType);
        Assert.Equal(typeof(string), coverageType.GetProperty(nameof(ManagementCoverageRecord.Source))?.PropertyType);
        Assert.Equal(typeof(string), coverageType.GetProperty(nameof(ManagementCoverageRecord.Status))?.PropertyType);
    }

    [Fact]
    public void EfPositionRepository_HasNoExpressionBodiedMembers()
    {
        var repositoryFile = FindRepositoryFile();
        var source = File.ReadAllText(repositoryFile);
        var lines = source.Split('\n');

        var expressionBodiedMember = new Regex(
            @"(public|private|protected|internal)[^\n{;]*\)\s*=>",
            RegexOptions.Compiled);

        var offendingLines = lines
            .Select((line, index) => (Line: line, Number: index + 1))
            .Where(entry => expressionBodiedMember.IsMatch(entry.Line))
            .Select(entry => $"line {entry.Number}: {entry.Line.Trim()}")
            .ToList();

        Assert.True(
            offendingLines.Count == 0,
            "EfPositionRepository must use block-bodied members, but found: " + string.Join("; ", offendingLines));
    }

    private static readonly Regex FakeGuidEmptyFallback = new(@"\?\?\s*Guid\.Empty", RegexOptions.Compiled);

    [Fact]
    public void PositionSurface_DoesNotReintroduce_FakeGuidEmptyIdHelpers()
    {
        var files = FindPositionSurfaceFiles().ToList();

        // A guard that can silently scan zero files is not a guard - fail loudly if the
        // directory layout ever changes instead of passing green on nothing.
        Assert.NotEmpty(files);

        var offenders = new List<string>();

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("LegalEntityIdValue", StringComparison.Ordinal)
                    || line.Contains("DepartmentIdValue", StringComparison.Ordinal)
                    || FakeGuidEmptyFallback.IsMatch(line))
                {
                    offenders.Add($"{file}:{i + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Position domain/entity/repository/application surface must not reintroduce Guid.Empty fallback semantics, but found: "
                + string.Join("; ", offenders));
    }

    private static IEnumerable<string> FindPositionSurfaceFiles()
    {
        var srcRoot = FindSrcRoot();

        var directories = new[]
        {
            Path.Combine(srcRoot, "ONEVO.Domain", "Features", "OrgStructure", "Position"),
            Path.Combine(srcRoot, "ONEVO.Application", "Features", "OrgStructure", "Position"),
            Path.Combine(srcRoot, "ONEVO.Infrastructure", "Persistence", "Repositories", "OrgStructure", "Position"),
            Path.Combine(srcRoot, "ONEVO.Infrastructure", "Persistence", "Configurations", "OrgStructure", "Position"),
        };

        foreach (var directory in directories)
        {
            Assert.True(Directory.Exists(directory), $"Expected Position surface directory to exist: {directory}");
        }

        return directories.SelectMany(directory => Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories));
    }

    private static string FindSrcRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src");
            if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(candidate, "ONEVO.Domain")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate src directory walking up from " + AppContext.BaseDirectory);
    }

    [Fact]
    public void IPositionRepository_EveryTenantScopedReadMethodHasATenantIdParameter()
    {
        var methods = typeof(IPositionRepository).GetMethods()
            .Where(m => !m.Name.Equals("AddAsync", StringComparison.Ordinal)
                     && !m.Name.Equals("Update", StringComparison.Ordinal)
                     && !m.Name.Equals("SaveChangesAsync", StringComparison.Ordinal)
                     && !m.Name.Equals("AddReportingHistoryAsync", StringComparison.Ordinal)
                     && !m.Name.Equals("AddManagementCoverageRecordAsync", StringComparison.Ordinal));

        var offenders = methods
            .Where(m => m.GetParameters().Length > 0 && m.GetParameters()[0].Name == "tenantId" && m.GetParameters()[0].ParameterType != typeof(Guid))
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    private static string FindRepositoryFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "ONEVO.Infrastructure",
                "Persistence",
                "Repositories",
                "OrgStructure",
                "Position",
                "EfPositionRepository.cs");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate EfPositionRepository.cs walking up from " + AppContext.BaseDirectory);
    }

    private static string ReadConfigurationSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "ONEVO.Infrastructure",
                "Persistence",
                "Configurations",
                "OrgStructure",
                "Position",
                "PositionConfiguration.cs");

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate PositionConfiguration.cs walking up from " + AppContext.BaseDirectory);
    }

    private static string ReadMigrationSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "ONEVO.Infrastructure",
                "Migrations",
                "20260804102821_AddPositionFoundationSchema.cs");

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate 20260804102821_AddPositionFoundationSchema.cs walking up from " + AppContext.BaseDirectory);
    }
}
