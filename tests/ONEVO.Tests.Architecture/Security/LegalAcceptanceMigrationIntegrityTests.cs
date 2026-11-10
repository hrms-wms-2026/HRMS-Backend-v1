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
