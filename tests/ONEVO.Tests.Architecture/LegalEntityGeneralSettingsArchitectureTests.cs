using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards the Part 2A legal_entities expansion migration
/// (ExpandLegalEntityForGeneralSettings):
/// - it adds no tables and drops no tables (legal_entities already exists;
///   the migration is additive-only),
/// - every AddColumn/DropColumn in it targets legal_entities only,
/// - the parent_legal_entity_id FK is nullable and non-cascading,
/// - the logo_file_id FK is nullable and non-cascading,
/// - the entity still matches the inventory-approved column set,
/// - LegalEntity is still tenant-owned so RLS/tenant-filter coverage applies.
/// </summary>
public class LegalEntityGeneralSettingsArchitectureTests
{
    [Fact]
    public void LegalEntity_IsTenantOwned_ForIsolationFilters()
    {
        Assert.True(typeof(ITenantOwnedEntity).IsAssignableFrom(typeof(LegalEntity)));
    }

    [Fact]
    public void LegalEntity_HasExactlyTheInventoryColumns()
    {
        var properties = typeof(LegalEntity)
            .GetProperties()
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var expected = new[]
        {
            "AddressJson",
            "CompanyCode",
            "CountryCode",
            "CreatedAt",
            "CurrencyCode",
            "DateFormat",
            "DefaultLanguage",
            "Email",
            "FinancialYearStartMonth",
            "FirstDayOfWeek",
            "Id",
            "IsActive",
            "IsPrimary",
            "LogoFileId",
            "Name",
            "ParentLegalEntityId",
            "PhoneNumber",
            "RegistrationNumber",
            "StandardWorkingDays",
            "TaxRegistrationNumber",
            "TenantId",
            "TimeFormat",
            "Timezone",
            "UpdatedAt",
            "VatGstNumber",
            "Website"
        };

        Assert.Equal(expected, properties);
    }

    [Fact]
    public void ExpandMigration_AddsNoTables_AndDropsNoTables()
    {
        var source = ReadExpandMigrationSource();

        Assert.DoesNotContain("CreateTable", source);
        Assert.DoesNotContain("DropTable", source);
    }

    [Fact]
    public void ExpandMigration_EveryAddOrDropColumn_TargetsLegalEntitiesOnly()
    {
        var source = ReadExpandMigrationSource();

        var offendingTables = Regex.Matches(source, @"(?:AddColumn|DropColumn)\w*\s*<?[^(]*\(\s*name:\s*""[^""]+"",\s*table:\s*""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .Where(table => table != "legal_entities")
            .Distinct()
            .ToList();

        Assert.Empty(offendingTables);
    }

    [Fact]
    public void ExpandMigration_ParentLegalEntityForeignKey_IsNullableAndDoesNotCascade()
    {
        var source = ReadExpandMigrationSource();

        var addColumnMatch = Regex.Match(
            source,
            @"AddColumn<Guid>\(\s*name:\s*""parent_legal_entity_id"",[^;]*?nullable:\s*(true|false)",
            RegexOptions.Singleline);
        Assert.True(addColumnMatch.Success, "expected an AddColumn<Guid> call for parent_legal_entity_id");
        Assert.Equal("true", addColumnMatch.Groups[1].Value);

        var fkMatch = Regex.Match(
            source,
            @"AddForeignKey\s*\((?<body>[^;]*?column:\s*""parent_legal_entity_id""[^;]*?)\);",
            RegexOptions.Singleline);
        Assert.True(fkMatch.Success, "expected an AddForeignKey call on legal_entities.parent_legal_entity_id");

        var body = fkMatch.Groups["body"].Value;
        Assert.Contains("principalTable: \"legal_entities\"", body);
        Assert.DoesNotContain("ReferentialAction.Cascade", body);
    }

    [Fact]
    public void ExpandMigration_LogoForeignKey_IsNullableAndDoesNotCascade()
    {
        var source = ReadExpandMigrationSource();

        var addColumnMatch = Regex.Match(
            source,
            @"AddColumn<Guid>\(\s*name:\s*""logo_file_id"",[^;]*?nullable:\s*(true|false)",
            RegexOptions.Singleline);
        Assert.True(addColumnMatch.Success, "expected an AddColumn<Guid> call for logo_file_id");
        Assert.Equal("true", addColumnMatch.Groups[1].Value);

        var fkMatch = Regex.Match(
            source,
            @"AddForeignKey\s*\((?<body>[^;]*?column:\s*""logo_file_id""[^;]*?)\);",
            RegexOptions.Singleline);
        Assert.True(fkMatch.Success, "expected an AddForeignKey call on legal_entities.logo_file_id");

        var body = fkMatch.Groups["body"].Value;
        Assert.Contains("principalTable: \"file_records\"", body);
        Assert.DoesNotContain("ReferentialAction.Cascade", body);
    }

    [Fact]
    public void ExpandMigration_NeverDisablesRowLevelSecurity()
    {
        var source = ReadExpandMigrationSource();

        Assert.DoesNotContain("DISABLE ROW LEVEL SECURITY", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP POLICY", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpandMigration_TenantIdNameUniqueIndex_HasDuplicatePrecheck()
    {
        var source = ReadExpandMigrationSource();

        var upStart = source.IndexOf("protected override void Up(", StringComparison.Ordinal);
        var indexStart = source.IndexOf("name: \"ix_legal_entities_tenant_id_name\"", StringComparison.Ordinal);
        Assert.True(upStart >= 0 && indexStart > upStart, "expected the tenant_id+name unique index in Up()");

        var precedingUpBody = source[upStart..indexStart];
        Assert.Contains("RAISE EXCEPTION", precedingUpBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HAVING COUNT(*) > 1", precedingUpBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Model_LegalEntities_ParentAndLogoIndexesExist()
    {
        using var context = CreateModelInspectionContext();

        var entityType = context.Model.GetEntityTypes().Single(e => e.ClrType == typeof(LegalEntity));
        var indexNames = entityType.GetIndexes()
            .Select(i => i.GetDatabaseName())
            .Where(n => n is not null)
            .ToHashSet();

        Assert.Contains("ix_legal_entities_tenant_id_name", indexNames);
        Assert.Contains("ix_legal_entities_tenant_id_company_code", indexNames);
        Assert.Contains("ix_legal_entities_tenant_id_registration_number", indexNames);
    }

    /// <summary>
    /// The canonical docs (database/schemas/org-structure.md) name this column
    /// week_start_day. The CLR property stays FirstDayOfWeek, so this guards the
    /// explicit HasColumnName mapping in LegalEntityConfiguration against
    /// regressing back to the snake_case-convention default of
    /// first_day_of_week.
    /// </summary>
    [Fact]
    public void Model_LegalEntities_FirstDayOfWeek_MapsToWeekStartDayColumn()
    {
        using var context = CreateModelInspectionContext();

        var entityType = context.Model.GetEntityTypes().Single(e => e.ClrType == typeof(LegalEntity));
        var property = entityType.FindProperty(nameof(LegalEntity.FirstDayOfWeek));

        Assert.NotNull(property);
        Assert.Equal("week_start_day", property!.GetColumnName());
    }

    private static string ReadExpandMigrationSource()
    {
        var migrationsDir = FindMigrationsDirectory();
        var migrationFiles = Directory.GetFiles(migrationsDir, "*ExpandLegalEntityForGeneralSettings.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Single(migrationFiles);
        return File.ReadAllText(migrationFiles[0]);
    }

    private static string FindMigrationsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ONEVO.Infrastructure", "Migrations");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate src/ONEVO.Infrastructure/Migrations above " + AppContext.BaseDirectory);
    }

    private static ApplicationDbContext CreateModelInspectionContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"arch-test-{Guid.NewGuid()}")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), new SystemDateTimeProvider()),
            new SoftDeleteInterceptor(new SystemDateTimeProvider()),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }
}
