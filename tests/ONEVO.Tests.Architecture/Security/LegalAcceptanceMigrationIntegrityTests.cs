using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace ONEVO.Tests.Architecture.Security;

public class LegalAcceptanceMigrationIntegrityTests
{
    private const string MigrationFileName = "20260724120000_AddLegalDocumentVersionsAndLegalAcceptanceRecords.cs";
    private const string MigrationDesignerFileName = "20260724120000_AddLegalDocumentVersionsAndLegalAcceptanceRecords.Designer.cs";
    private const string InitialSchemaFileName = "20260509213212_InitialSchema.cs";
    private const string SnapshotFileName = "ApplicationDbContextModelSnapshot.cs";

    [Fact]
    public void LatestMigration_HasBothMigrationFileAndDesignerFile()
    {
        var migrationsDir = FindMigrationsDirectory();

        File.Exists(Path.Combine(migrationsDir, MigrationFileName)).Should().BeTrue(
            $"'{MigrationFileName}' must exist");
        File.Exists(Path.Combine(migrationsDir, MigrationDesignerFileName)).Should().BeTrue(
            $"'{MigrationDesignerFileName}' must exist alongside the migration, or EF tooling cannot " +
            "recognize this migration (the [Migration]/[DbContext] attributes live on the Designer partial)");

        var designerCode = File.ReadAllText(Path.Combine(migrationsDir, MigrationDesignerFileName));
        designerCode.Should().Contain("[DbContext(typeof(ApplicationDbContext))]");
        designerCode.Should().Contain("[Migration(\"20260724120000_AddLegalDocumentVersionsAndLegalAcceptanceRecords\")]");
        designerCode.Should().Contain("partial class AddLegalDocumentVersionsAndLegalAcceptanceRecords");
    }

    [Fact]
    public void PublishedIndexName_IsConsistentAcrossConfigurationMigrationDesignerAndSnapshot()
    {
        const string expectedIndexName = "ix_legal_document_versions_document_type_published";

        var srcDir = FindSrcDirectory();
        var configPath = Path.Combine(
            srcDir,
            "ONEVO.Infrastructure",
            "Persistence",
            "Configurations",
            "DevPlatform",
            "Compliance",
            "LegalDocumentVersionConfiguration.cs");

        File.Exists(configPath).Should().BeTrue("LegalDocumentVersionConfiguration.cs must exist");
        var configCode = File.ReadAllText(configPath);
        var migrationCode = File.ReadAllText(FindMigrationFile(MigrationFileName));
        var designerCode = File.ReadAllText(FindMigrationFile(MigrationDesignerFileName));
        var snapshotCode = ReadSnapshotFile();

        configCode.Should().Contain(
            $"HasDatabaseName(\"{expectedIndexName}\")",
            "the EF configuration must explicitly name the partial unique published index so it can never " +
            "drift from the physical name the migration actually creates in Postgres");
        migrationCode.Should().Contain(
            expectedIndexName,
            "the hand-written migration body must physically create the index under this exact name");
        designerCode.Should().Contain(
            expectedIndexName,
            "the migration's Designer metadata (target model) must agree with the configuration and the migration body");
        snapshotCode.Should().Contain(
            expectedIndexName,
            "the model snapshot must agree with the configuration, the migration body, and the Designer metadata");
    }

    [Fact]
    public void LatestSnapshot_ContainsLegalAcceptanceRecordsAndLegalDocumentVersions()
    {
        var snapshotCode = ReadSnapshotFile();

        snapshotCode.Should().Contain("ONEVO.Domain.Features.Auth.Entities.LegalAcceptanceRecord");
        snapshotCode.Should().Contain("b.ToTable(\"legal_acceptance_records\"");

        snapshotCode.Should().Contain("ONEVO.Domain.Features.DevPlatform.Compliance.Entities.LegalDocumentVersion");
        snapshotCode.Should().Contain("b.ToTable(\"legal_document_versions\"");
    }

    [Fact]
    public void LatestSnapshot_DoesNotContainGdprConsentRecordEntityOrTable()
    {
        var snapshotCode = ReadSnapshotFile();

        snapshotCode.Should().NotContain(
            "GdprConsentRecord",
            "the snapshot must reflect the current model, which no longer defines this entity");
        snapshotCode.Should().NotContain(
            "gdpr_consent_records",
            "the snapshot must reflect the current model, where this table has been renamed to legal_acceptance_records");
    }

    [Fact]
    public void Up_DropsOldConsentTypeIndexBeforeDroppingConsentTypeColumn()
    {
        var upBody = ReadUpMethodBody();

        var dropIndexPosition = upBody.IndexOf(
            "ix_gdpr_consent_records_tenant_id_user_id_consent_type",
            StringComparison.Ordinal);
        var dropColumnMatch = Regex.Match(upBody, "DropColumn\\(\\s*name:\\s*\"consent_type\"");
        var dropColumnPosition = dropColumnMatch.Success ? dropColumnMatch.Index : -1;

        dropIndexPosition.Should().BeGreaterThan(-1, "the old index name must appear in Up()");
        dropColumnPosition.Should().BeGreaterThan(-1, "consent_type must be dropped in Up()");
        dropIndexPosition.Should().BeLessThan(
            dropColumnPosition,
            "the old consent_type index must be dropped before the consent_type column is dropped, " +
            "otherwise Postgres will fail with 'cannot drop column consent_type because other objects depend on it'");
    }

    [Fact]
    public void Up_CreatesLegalDocumentVersionsWithExactly13Columns()
    {
        var upBody = ReadUpMethodBody();
        var tableBody = ExtractCreateTableBody(upBody, "legal_document_versions");

        CountColumnDeclarations(tableBody).Should().Be(13);
    }

    [Fact]
    public void Up_ReconciliesLegalAcceptanceRecordsToExactly11CanonicalColumns()
    {
        var initialSchemaBody = File.ReadAllText(FindMigrationFile(InitialSchemaFileName));
        var originalTableBody = ExtractCreateTableBody(initialSchemaBody, "gdpr_consent_records");
        var originalColumnCount = CountColumnDeclarations(originalTableBody);

        var upBody = ReadUpMethodBody();
        var addedColumnCount = Regex.Matches(
            upBody,
            "AddColumn<[^>]+>\\(\\s*name:\\s*\"[a-z_]+\",\\s*table:\\s*\"legal_acceptance_records\"").Count;
        var droppedColumnCount = Regex.Matches(
            upBody,
            "DropColumn\\(\\s*name:\\s*\"[a-z_]+\",\\s*table:\\s*\"legal_acceptance_records\"").Count;

        originalColumnCount.Should().Be(7, "gdpr_consent_records originally had 7 columns");
        addedColumnCount.Should().Be(7, "document_type, document_version, decision, required, decided_at, user_agent, source");
        droppedColumnCount.Should().Be(3, "consent_type, consented, consented_at are retired");

        (originalColumnCount + addedColumnCount - droppedColumnCount).Should().Be(11);
    }

    [Fact]
    public void Up_AppliesPartialUniquePublishedIndexOnDocumentType()
    {
        var upBody = ReadUpMethodBody();

        upBody.Should().Contain("CREATE UNIQUE INDEX ix_legal_document_versions_document_type_published");
        upBody.Should().Contain("ON legal_document_versions (document_type)");
        upBody.Should().Contain("WHERE status = 'published';");
    }

    [Fact]
    public void Up_ReAppliesRlsPolicyToLegalAcceptanceRecordsOnly()
    {
        var upBody = ReadUpMethodBody();
        var fullCode = File.ReadAllText(FindMigrationFile(MigrationFileName));

        var tenantTablesMatch = Regex.Match(
            fullCode,
            "TenantTables\\s*=\\s*\\[(?<items>[^\\]]*)\\]",
            RegexOptions.Singleline);

        tenantTablesMatch.Success.Should().BeTrue("the migration must declare its RLS TenantTables array");
        var declaredTables = Regex.Matches(tenantTablesMatch.Groups["items"].Value, "\"([a-z_]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();

        declaredTables.Should().BeEquivalentTo(["legal_acceptance_records"]);

        upBody.Should().Contain("ENABLE ROW LEVEL SECURITY");
        upBody.Should().Contain("FORCE ROW LEVEL SECURITY");
        upBody.Should().Contain("CREATE POLICY tenant_isolation ON {table}");
    }

    [Fact]
    public void Down_RestoresOriginalGdprConsentRecordsSchema()
    {
        var downBody = ReadDownMethodBody();

        downBody.Should().Contain("name: \"consent_type\"");
        downBody.Should().Contain("name: \"consented\"");
        downBody.Should().Contain("name: \"consented_at\"");
        downBody.Should().Contain("newName: \"gdpr_consent_records\"");
        downBody.Should().Contain("ix_gdpr_consent_records_tenant_id_user_id_consent_type");
        downBody.Should().Contain("DropTable(name: \"legal_document_versions\")");
        downBody.Should().Contain("ENABLE ROW LEVEL SECURITY");
        downBody.Should().Contain("CREATE POLICY tenant_isolation ON gdpr_consent_records");

        foreach (var addedColumn in new[] { "document_type", "document_version", "decision", "required", "decided_at", "user_agent", "source" })
        {
            downBody.Should().Contain(
                $"DropColumn(name: \"{addedColumn}\", table: \"legal_acceptance_records\")",
                $"Down() must drop the Up()-added column '{addedColumn}' before restoring the legacy shape");
        }
    }

    [Fact]
    public void NoActiveGdprConsentRecordReferencesOutsideHistoricalMigrations()
    {
        var srcDir = FindSrcDirectory();
        var migrationsDir = Path.Combine(srcDir, "ONEVO.Infrastructure", "Migrations");

        var offendingFiles = Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.StartsWith(migrationsDir, StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Contains("GdprConsentRecord"))
            .ToList();

        offendingFiles.Should().BeEmpty(
            "GdprConsentRecord has been retired; active code must use LegalAcceptanceRecord instead");
    }

    [Fact]
    public void LegalLoginChallengesTable_ExistsAsTheApprovedStep2PreSessionLegalBlockingStore()
    {
        // Supersedes the former NoLegalLoginChallengesTableExistsAnywhereInSource guard: that test
        // pinned the Step 1 state (legal_login_challenges deliberately absent). Step 2 (production
        // readiness pass) adds the OneVo-HR-documented legal_login_challenges table
        // (database/schemas/auth.md) and wires it into ILoginContinuationService, so its presence
        // is now required, not forbidden.
        var srcDir = FindSrcDirectory();

        var entityFile = Path.Combine(srcDir, "ONEVO.Domain", "Features", "Auth", "Login", "Entities", "LegalLoginChallenge.cs");
        var repositoryInterfaceFile = Path.Combine(
            srcDir, "ONEVO.Application", "Features", "Auth", "Legal", "RepositoryInterfaces", "ILegalLoginChallengeRepository.cs");

        File.Exists(entityFile).Should().BeTrue("the legal_login_challenges entity must exist");
        File.Exists(repositoryInterfaceFile).Should().BeTrue("the legal_login_challenges repository interface must exist");

        var migrationsDir = Path.Combine(srcDir, "ONEVO.Infrastructure", "Migrations");
        var migrationExists = Directory.GetFiles(migrationsDir, "*.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            .Any(f => File.ReadAllText(f).Contains("\"legal_login_challenges\""));

        migrationExists.Should().BeTrue("a migration must create the legal_login_challenges table");
    }

    private static string ExtractCreateTableBody(string code, string tableName)
    {
        var marker = $"name: \"{tableName}\",";
        var start = code.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"CreateTable for '{tableName}' must exist");

        var end = code.IndexOf("constraints:", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(-1);

        return code[start..end];
    }

    private static int CountColumnDeclarations(string tableBody)
    {
        return Regex.Matches(tableBody, "=\\s*table\\.Column<").Count;
    }

    private static string ReadUpMethodBody()
    {
        var code = File.ReadAllText(FindMigrationFile(MigrationFileName));
        var start = code.IndexOf("protected override void Up(", StringComparison.Ordinal);
        var end = code.IndexOf("protected override void Down(", StringComparison.Ordinal);

        start.Should().BeGreaterThan(-1);
        end.Should().BeGreaterThan(start);

        return code[start..end];
    }

    private static string ReadDownMethodBody()
    {
        var code = File.ReadAllText(FindMigrationFile(MigrationFileName));
        var start = code.IndexOf("protected override void Down(", StringComparison.Ordinal);

        start.Should().BeGreaterThan(-1);

        return code[start..];
    }

    private static string ReadSnapshotFile()
    {
        var migrationsDir = FindMigrationsDirectory();
        var path = Path.Combine(migrationsDir, SnapshotFileName);
        File.Exists(path).Should().BeTrue($"'{SnapshotFileName}' must exist under {migrationsDir}");
        return File.ReadAllText(path);
    }

    private static string FindMigrationFile(string fileName)
    {
        var migrationsDir = FindMigrationsDirectory();
        var path = Path.Combine(migrationsDir, fileName);
        File.Exists(path).Should().BeTrue($"'{fileName}' must exist under {migrationsDir}");
        return path;
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

        throw new DirectoryNotFoundException("Could not locate src/ONEVO.Infrastructure/Migrations above " + AppContext.BaseDirectory);
    }

    private static string FindSrcDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src above " + AppContext.BaseDirectory);
    }
}
