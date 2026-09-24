using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards the Part 2A scope for Department: schema/entity/repository only, no
/// controller or API route, nullable HeadPositionId with Restrict FK to positions.id,
/// block-bodied repository methods with explicit tenantId/legalEntityId filtering,
/// and RLS enabled on the migration that creates the table.
/// </summary>
public sealed class DepartmentPart2AArchitectureTests
{
    private static readonly Type DepartmentType = typeof(Department);
    private static readonly Type RepositoryType = typeof(EfDepartmentRepository);

    [Fact]
    public void DepartmentEntity_LivesInOrgStructureEntitiesNamespace()
    {
        Assert.Equal("ONEVO.Domain.Features.OrgStructure.Entities", DepartmentType.Namespace);
    }

    [Fact]
    public void DepartmentEntity_IsTenantOwned()
    {
        Assert.True(typeof(ITenantOwnedEntity).IsAssignableFrom(DepartmentType));
    }

    [Fact]
    public void DepartmentEntity_HasHeadPositionIdProperty_ThatIsNullableGuid()
    {
        var property = DepartmentType.GetProperty("HeadPositionId");
        Assert.NotNull(property);
        Assert.Equal(typeof(Guid?), property.PropertyType);
    }

    [Fact]
    public void DepartmentEntity_HasNoOtherPositionOrEmployeeCountProperties()
    {
        var offenders = DepartmentType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "HeadPositionId" && (
                p.Name.Contains("Position", StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("Employee", StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    // NoDepartmentsController_ExistsYet and NoDepartmentApiRoute_ExistsYet were removed here:
    // they asserted no Department controller or route existed, which was correct for Part 2A/2B scope
    // but is now obsolete now that Part 2C legitimately adds
    // Controllers/Tenant/OrgStructure/DepartmentsController.cs. Controller/route
    // and permission-mapping rules for that controller now live in
    // DepartmentsControllerArchitectureTests.cs.

    [Fact]
    public void EfDepartmentRepository_HasNoExpressionBodiedMembers()
    {
        var path = FindRepositoryFile();
        var source = File.ReadAllText(path);
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
            "EfDepartmentRepository must use block-bodied members, but found: " + string.Join("; ", offendingLines));
    }

    private static readonly string[] MethodsScopedByEntityInsteadOfParameter =
        [nameof(IDepartmentRepository.AddAsync), nameof(IDepartmentRepository.Update), nameof(IDepartmentRepository.SaveChangesAsync)];

    [Fact]
    public void IDepartmentRepository_EveryReadMethodHasATenantIdParameter()
    {
        var methods = typeof(IDepartmentRepository).GetMethods()
            .Where(m => !MethodsScopedByEntityInsteadOfParameter.Contains(m.Name));

        var offenders = methods
            .Where(m => !m.GetParameters().Any(p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase)))
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData(nameof(IDepartmentRepository.ListByLegalEntityAsync))]
    [InlineData(nameof(IDepartmentRepository.GetByIdForLegalEntityAsync))]
    [InlineData(nameof(IDepartmentRepository.ExistsByNameAsync))]
    [InlineData(nameof(IDepartmentRepository.ExistsAsync))]
    [InlineData(nameof(IDepartmentRepository.ExistsByCodeAsync))]
    [InlineData(nameof(IDepartmentRepository.IsDescendantAsync))]
    [InlineData(nameof(IDepartmentRepository.CountActiveChildrenAsync))]
    [InlineData(nameof(IDepartmentRepository.CountActiveEmployeesAsync))]
    [InlineData(nameof(IDepartmentRepository.ListPageByLegalEntityAsync))]
    [InlineData(nameof(IDepartmentRepository.ListForTreeByLegalEntityAsync))]
    public void IDepartmentRepository_LegalEntityScopedMethods_HaveALegalEntityIdParameter(string methodName)
    {
        var method = typeof(IDepartmentRepository).GetMethods().First(m => m.Name == methodName);

        var hasLegalEntityId = method.GetParameters()
            .Any(p => string.Equals(p.Name, "legalEntityId", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasLegalEntityId, $"{methodName} must accept a legalEntityId parameter.");
    }

    [Fact]
    public void AddDepartmentsMigration_EnablesRowLevelSecurityWithTenantIsolationPolicy()
    {
        var source = ReadMigrationSourceContaining("CreateTable(", "name: \"departments\"");

        Assert.Contains("TenantTables = [\"departments\"]", source);
        Assert.Contains("ALTER TABLE {table} ENABLE ROW LEVEL SECURITY", source);
        Assert.Contains("ALTER TABLE {table} FORCE ROW LEVEL SECURITY", source);
        Assert.Contains("CREATE POLICY tenant_isolation ON {table}", source);
    }

    [Fact]
    public void CorrectiveMigration_AddsHeadPositionIdColumn_AsNullableWithRestrictForeignKey()
    {
        var source = ReadMigrationSourceContaining("AddColumn", "head_position_id", "departments");

        Assert.Contains("name: \"head_position_id\"", source);
        Assert.Contains("table: \"departments\"", source);
        Assert.Contains("nullable: true", source);
        Assert.Contains("principalTable: \"positions\"", source);
        Assert.Contains("principalColumn: \"id\"", source);
        Assert.Contains("onDelete: ReferentialAction.Restrict", source);
    }

    [Fact]
    public void DepartmentEfConfiguration_MapsHeadPositionId_WithIndexAndRestrictForeignKey()
    {
        var source = ReadDepartmentConfigurationSource();

        Assert.Contains("HeadPositionId", source);
        Assert.Contains("ix_departments_head_position_id", source);
        Assert.Contains("HasForeignKey(d => d.HeadPositionId)", source);
        Assert.Contains("OnDelete(DeleteBehavior.Restrict)", source);
    }

    [Fact]
    public void DepartmentEfConfiguration_PreservesUniqueNameIndexScopedToTenantAndLegalEntity()
    {
        var source = ReadDepartmentConfigurationSource();

        Assert.Contains("TenantId, d.LegalEntityId, d.Name", source);
        Assert.Contains("IsUnique()", source);
        Assert.Contains("ix_departments_tenant_id_legal_entity_id_name", source);
    }

    [Fact]
    public void AddDepartmentsMigration_DoesNotModifyLegalEntitiesOrPositionsOrEmployeesColumns()
    {
        var source = ReadMigrationSourceContaining("CreateTable(", "name: \"departments\"");

        Assert.DoesNotContain("AddColumn", source);
        Assert.DoesNotContain("DropColumn", source);
        Assert.DoesNotContain("AlterColumn", source);
    }

    [Fact]
    public void CodeUniqueIndexMigration_PrechecksDuplicatesAndCreatesCaseInsensitiveExpressionIndex()
    {
        var source = ReadMigrationSourceContaining("ux_departments_tenant_legal_entity_code_lower");

        Assert.Contains("RAISE EXCEPTION", source);
        Assert.Contains("lower(code)", source);
        Assert.Contains("CREATE UNIQUE INDEX ux_departments_tenant_legal_entity_code_lower", source);
        Assert.Contains("WHERE code IS NOT NULL", source);
        Assert.Contains("DROP INDEX IF EXISTS ux_departments_tenant_legal_entity_code_lower", source);
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
                "Department",
                "EfDepartmentRepository.cs");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate EfDepartmentRepository.cs walking up from " + AppContext.BaseDirectory);
    }

    private static string ReadDepartmentConfigurationSource()
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
                "Department",
                "DepartmentConfiguration.cs");

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate DepartmentConfiguration.cs walking up from " + AppContext.BaseDirectory);
    }

    private static string ReadMigrationSourceContaining(params string[] mustContain)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var migrationsDir = Path.Combine(directory.FullName, "src", "ONEVO.Infrastructure", "Migrations");
            if (Directory.Exists(migrationsDir))
            {
                foreach (var file in Directory.GetFiles(migrationsDir, "*.cs"))
                {
                    if (file.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var text = File.ReadAllText(file);
                    if (mustContain.All(fragment => text.Contains(fragment)))
                    {
                        return text;
                    }
                }
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate migration source file walking up from " + AppContext.BaseDirectory);
    }
}
